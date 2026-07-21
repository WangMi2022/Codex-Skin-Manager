import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  buildPayload as buildThemeV2Payload,
  REMOVE_EXPRESSION as REMOVE_THEME_V2_EXPRESSION,
  VERIFY_REMOVED_EXPRESSION as VERIFY_THEME_V2_REMOVED_EXPRESSION,
  verifyExpression as themeV2VerifyExpression,
} from "./theme-v2/payload.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const SKIN_VERSION = "2.0.0";
const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "[::1]", "::1"]);
const BROWSER_ID_PATTERN = /^[A-Za-z0-9._-]{1,200}$/;
const SKIN_ID_PATTERN = /^[a-z0-9][a-z0-9._-]{0,63}$/;
const MAX_SKIN_CSS_BYTES = 2 * 1024 * 1024;
const MAX_SKIN_ART_BYTES = 20 * 1024 * 1024;
const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

class CdpIdentityMismatchError extends Error {}

function parseArgs(argv) {
  const options = {
    port: 9335,
    mode: "watch",
    timeoutMs: 30000,
    screenshot: null,
    reload: false,
    browserId: null,
    skinStateRoot: process.env.CODEX_DREAM_SKIN_STATE_ROOT
      ? path.resolve(process.env.CODEX_DREAM_SKIN_STATE_ROOT)
      : null,
  };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === "--port") options.port = Number(argv[++i]);
    else if (arg === "--once") options.mode = "once";
    else if (arg === "--watch") options.mode = "watch";
    else if (arg === "--verify") options.mode = "verify";
    else if (arg === "--remove") options.mode = "remove";
    else if (arg === "--timeout-ms") options.timeoutMs = Number(argv[++i]);
    else if (arg === "--browser-id") options.browserId = argv[++i];
    else if (arg === "--screenshot") options.screenshot = path.resolve(argv[++i]);
    else if (arg === "--reload") options.reload = true;
    else if (arg === "--self-test") options.mode = "self-test";
    else if (arg === "--check-payload") options.mode = "check-payload";
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!Number.isInteger(options.port) || options.port < 1024 || options.port > 65535) {
    throw new Error(`Invalid port: ${options.port}`);
  }
  if (!Number.isInteger(options.timeoutMs) || options.timeoutMs < 250 || options.timeoutMs > 120000) {
    throw new Error(`Invalid timeout: ${options.timeoutMs}`);
  }
  if (options.browserId !== null && !BROWSER_ID_PATTERN.test(options.browserId)) {
    throw new Error(`Invalid browser ID: ${options.browserId}`);
  }
  if (["watch", "once", "verify", "remove"].includes(options.mode) && !options.browserId) {
    throw new Error(`--browser-id is required in ${options.mode} mode`);
  }
  return options;
}

function validatedDebuggerUrl(target, port) {
  const url = new URL(target.webSocketDebuggerUrl);
  const pathIsValid = /^\/devtools\/(?:page|browser)\/[A-Za-z0-9._-]{1,200}$/.test(url.pathname);
  if (url.protocol !== "ws:" || !LOOPBACK_HOSTS.has(url.hostname) || Number(url.port) !== port ||
      url.username || url.password || url.search || url.hash || !pathIsValid) {
    throw new Error("Rejected a CDP WebSocket URL outside the allowed loopback endpoint shape");
  }
  return url.href;
}

function browserIdFromVersion(version, port) {
  const url = validatedDebuggerUrl(version, port);
  const parsed = new URL(url);
  const match = parsed.pathname.match(/^\/devtools\/browser\/([A-Za-z0-9._-]{1,200})$/);
  if (!match || parsed.search || parsed.hash || !BROWSER_ID_PATTERN.test(match[1])) {
    throw new Error("Rejected an invalid CDP browser identity URL");
  }
  return match[1];
}

function isValidCdpPageTarget(item, port) {
  if (item?.type !== "page" || !item.url?.startsWith("app://") || typeof item.id !== "string" ||
      !BROWSER_ID_PATTERN.test(item.id) || !item.webSocketDebuggerUrl) return false;
  try {
    const debuggerUrl = new URL(validatedDebuggerUrl(item, port));
    return debuggerUrl.pathname === `/devtools/page/${item.id}`;
  } catch {
    return false;
  }
}

class CdpSession {
  constructor(target, port) {
    this.target = target;
    this.ws = new WebSocket(validatedDebuggerUrl(target, port));
    this.nextId = 1;
    this.pending = new Map();
    this.listeners = new Map();
    this.closed = false;
  }

  async open() {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        try { this.ws.close(); } catch {}
        reject(new Error("CDP WebSocket open timed out"));
      }, 5000);
      this.ws.addEventListener("open", () => { clearTimeout(timeout); resolve(); }, { once: true });
      this.ws.addEventListener("error", () => { clearTimeout(timeout); reject(new Error("CDP WebSocket open failed")); }, { once: true });
    });
    this.ws.addEventListener("message", (event) => this.onMessage(event));
    this.ws.addEventListener("error", () => this.close());
    this.ws.addEventListener("close", () => {
      this.closed = true;
      for (const waiter of this.pending.values()) {
        clearTimeout(waiter.timeout);
        waiter.reject(new Error("CDP socket closed"));
      }
      this.pending.clear();
    });
    await this.send("Runtime.enable");
    await this.send("Page.enable");
    return this;
  }

  onMessage(event) {
    let message;
    try {
      message = JSON.parse(String(event.data));
    } catch {
      this.close();
      return;
    }
    if (message.id) {
      const waiter = this.pending.get(message.id);
      if (!waiter) return;
      clearTimeout(waiter.timeout);
      this.pending.delete(message.id);
      if (message.error) waiter.reject(new Error(`${message.error.message} (${message.error.code})`));
      else waiter.resolve(message.result);
      return;
    }
    for (const listener of this.listeners.get(message.method) ?? []) listener(message.params ?? {});
  }

  on(method, listener) {
    const listeners = this.listeners.get(method) ?? [];
    listeners.push(listener);
    this.listeners.set(method, listeners);
  }

  send(method, params = {}) {
    if (this.closed) return Promise.reject(new Error("CDP session is closed"));
    return new Promise((resolve, reject) => {
      const id = this.nextId++;
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`CDP command timed out: ${method}`));
      }, 10000);
      this.pending.set(id, { resolve, reject, timeout });
      try {
        this.ws.send(JSON.stringify({ id, method, params }));
      } catch (error) {
        clearTimeout(timeout);
        this.pending.delete(id);
        reject(error);
      }
    });
  }

  async evaluate(expression) {
    const result = await this.send("Runtime.evaluate", {
      expression,
      awaitPromise: true,
      returnByValue: true,
      userGesture: false,
    });
    if (result.exceptionDetails) {
      const detail = result.exceptionDetails.exception?.description ?? result.exceptionDetails.text;
      throw new Error(`Renderer evaluation failed: ${detail}`);
    }
    return result.result?.value;
  }

  close() {
    for (const waiter of this.pending.values()) {
      clearTimeout(waiter.timeout);
      waiter.reject(new Error("CDP session closed"));
    }
    this.pending.clear();
    if (!this.closed) {
      try { this.ws.close(); } catch {}
    }
    this.closed = true;
  }
}

class BrowserIdentityAnchor {
  constructor(url) {
    this.ws = new WebSocket(url);
    this.closed = false;
    this.ws.addEventListener("close", () => { this.closed = true; });
    this.ws.addEventListener("error", () => {
      this.closed = true;
      try { this.ws.close(); } catch {}
    });
  }

  async open() {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.close();
        reject(new Error("CDP browser identity WebSocket open timed out"));
      }, 5000);
      this.ws.addEventListener("open", () => { clearTimeout(timeout); resolve(); }, { once: true });
      this.ws.addEventListener("error", () => {
        clearTimeout(timeout);
        reject(new Error("CDP browser identity WebSocket open failed"));
      }, { once: true });
      this.ws.addEventListener("close", () => {
        clearTimeout(timeout);
        reject(new Error("CDP browser identity WebSocket closed during startup"));
      }, { once: true });
    });
    if (this.closed) throw new Error("CDP browser identity WebSocket is already closed");
    return this;
  }

  close() {
    if (!this.closed) {
      try { this.ws.close(); } catch {}
    }
    this.closed = true;
  }
}

async function fetchCdpJson(port, resource) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 2000);
  try {
    const response = await fetch(`http://127.0.0.1:${port}${resource}`, {
      redirect: "error",
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return await response.json();
  } finally {
    clearTimeout(timeout);
  }
}

async function listAppTargets(port, expectedBrowserId = null) {
  const targets = await fetchCdpJson(port, "/json/list");
  if (!Array.isArray(targets)) throw new Error("CDP target list is not an array");
  if (expectedBrowserId) {
    const version = await fetchCdpJson(port, "/json/version");
    const actualBrowserId = browserIdFromVersion(version, port);
    if (actualBrowserId !== expectedBrowserId) {
      throw new CdpIdentityMismatchError(
        `CDP browser identity changed from ${expectedBrowserId} to ${actualBrowserId}`,
      );
    }
  }
  return targets.filter((item) => isValidCdpPageTarget(item, port));
}

async function connectBrowserIdentityAnchor(port, expectedBrowserId) {
  const version = await fetchCdpJson(port, "/json/version");
  const actualBrowserId = browserIdFromVersion(version, port);
  if (actualBrowserId !== expectedBrowserId) {
    throw new CdpIdentityMismatchError(
      `CDP browser identity changed from ${expectedBrowserId} to ${actualBrowserId}`,
    );
  }
  return new BrowserIdentityAnchor(validatedDebuggerUrl(version, port)).open();
}

function normalizedSkinText(value, fallback, maximumLength) {
  if (typeof value !== "string") return fallback;
  const normalized = value.trim().replace(/[\u0000-\u001f\u007f]/g, " ");
  return normalized ? normalized.slice(0, maximumLength) : fallback;
}

function validateSkinManifest(value, expectedId) {
  if (!value || Array.isArray(value) || typeof value !== "object") {
    throw new Error("Skin manifest must be a JSON object");
  }
  if (value.schemaVersion !== 1 || !SKIN_ID_PATTERN.test(value.id) || value.id !== expectedId) {
    throw new Error(`Invalid skin manifest identity: ${expectedId}`);
  }
  return {
    schemaVersion: 1,
    id: value.id,
    name: normalizedSkinText(value.name, value.id, 60),
    version: normalizedSkinText(value.version, "1.0.0", 24),
    author: normalizedSkinText(value.author, "Local skin", 60),
    description: normalizedSkinText(value.description, "", 160),
    brandName: normalizedSkinText(value.brandName, value.name || value.id, 80),
    brandSubtitle: normalizedSkinText(value.brandSubtitle, "Codex Dream Skin", 100),
    signature: normalizedSkinText(value.signature, value.name || value.id, 60),
  };
}

function assertSafeSkinCss(css) {
  if (/@import\b/i.test(css) || /url\s*\(\s*(['"])?\s*(?:https?:|javascript:|data:text\/html)/i.test(css)) {
    throw new Error("Skin CSS cannot import or request remote content");
  }
}

function resolveThemeV2File(directory, relative, label) {
  if (typeof relative !== "string" || !relative.trim() || path.isAbsolute(relative)) {
    throw new Error(`${label} must be a relative file inside the theme directory`);
  }
  const resolved = path.resolve(directory, relative);
  const prefix = `${path.resolve(directory)}${path.sep}`;
  if (!resolved.startsWith(prefix)) throw new Error(`${label} resolved outside the theme directory`);
  return resolved;
}

function assertSafeThemeV2Chrome(html) {
  if (/<\s*(?:script|iframe|object|embed|link|meta|base)\b/i.test(html) ||
      /\son[a-z0-9_-]+\s*=/i.test(html) || /(?:javascript|vbscript)\s*:/i.test(html) ||
      /(?:src|href)\s*=\s*(["'])?\s*(?:https?:|data:text\/html)/i.test(html)) {
    throw new Error("Theme v2 chrome.html contains executable or remote content");
  }
}

async function assertSafeThemeV2(directory, expectedId) {
  const manifestText = await readStrictUtf8(path.join(directory, "theme.json"), 256 * 1024);
  const manifest = JSON.parse(manifestText);
  if (!manifest || Array.isArray(manifest) || manifest.schemaVersion !== 2 ||
      typeof manifest.id !== "string" || !SKIN_ID_PATTERN.test(manifest.id) || manifest.id !== expectedId) {
    throw new Error("Theme v2 manifest version or ID is invalid");
  }
  const cssPath = resolveThemeV2File(directory, manifest.css || "theme.css", "theme.css");
  assertSafeSkinCss(await readStrictUtf8(cssPath, MAX_SKIN_CSS_BYTES));
  if (manifest.chrome) {
    const chromePath = resolveThemeV2File(directory, manifest.chrome, "chrome.html");
    assertSafeThemeV2Chrome(await readStrictUtf8(chromePath, 512 * 1024));
  }
  return manifest;
}

async function readStrictUtf8(filePath, maximumBytes) {
  const bytes = await fs.readFile(filePath);
  if (bytes.length === 0 || bytes.length > maximumBytes) {
    throw new Error(`Skin file has an invalid size: ${path.basename(filePath)}`);
  }
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}

async function resolveBuiltInDirectory() {
  const development = path.join(root, "skins", "rose-garden");
  try {
    const info = await fs.stat(development);
    if (info.isDirectory()) return development;
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
  }
  return path.join(root, "assets", "builtin", "rose-garden");
}

async function loadSkinSource(options) {
  const builtInDirectory = await resolveBuiltInDirectory();
  const defaultSource = {
    format: "dream-v1",
    directory: builtInDirectory,
    manifestPath: path.join(builtInDirectory, "skin.json"),
    cssPath: path.join(builtInDirectory, "dream-skin.css"),
    artPath: path.join(builtInDirectory, "art.png"),
    expectedId: "rose-garden",
    starlightEnabled: true,
  };
  const localAppData = process.env.LOCALAPPDATA;
  if (!options.skinStateRoot && !localAppData) return defaultSource;

  const stateRoot = options.skinStateRoot ?? path.resolve(localAppData, "CodexDreamSkin");
  const activePath = path.join(stateRoot, "active-skin.json");
  let active;
  try {
    active = JSON.parse(await readStrictUtf8(activePath, 64 * 1024));
  } catch (error) {
    if (error?.code === "ENOENT") return defaultSource;
    throw new Error(`Active skin configuration is invalid: ${error.message}`);
  }
  if (!active || Array.isArray(active) || active.schemaVersion !== 1 ||
      typeof active.skinId !== "string" || !SKIN_ID_PATTERN.test(active.skinId)) {
    throw new Error("Active skin configuration has an invalid skinId");
  }
  const starlightEnabled = active.starlightEnabled !== false;

  const skinsRoot = path.resolve(stateRoot, options.skinStateRoot ? "skin" : "skins");
  const directory = path.resolve(skinsRoot, active.skinId);
  const relative = path.relative(skinsRoot, directory);
  if (!relative || relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error("Active skin resolved outside the managed skin directory");
  }
  try {
    const stat = await fs.stat(path.join(directory, "theme.json"));
    if (stat.isFile()) return { format: "awesome-v2", directory, expectedId: active.skinId, starlightEnabled };
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
  }
  return {
    format: "dream-v1", directory,
    manifestPath: path.join(directory, "skin.json"),
    cssPath: path.join(directory, "dream-skin.css"),
    artPath: path.join(directory, "art.png"),
    expectedId: active.skinId,
    starlightEnabled,
  };
}

async function loadPayload(options) {
  const source = await loadSkinSource(options);
  if (source.format === "awesome-v2") {
    const manifest = await assertSafeThemeV2(source.directory, source.expectedId);
    const result = await buildThemeV2Payload(source.directory, {
      starlightEnabled: source.starlightEnabled !== false,
    });
    if (result.theme?.id !== source.expectedId) throw new Error("Theme v2 payload ID does not match the active skin");
    return {
      payload: result.payload,
      skin: { id: result.theme.id, name: result.theme.name || manifest.name || result.theme.id,
        starlightEnabled: source.starlightEnabled !== false },
      format: source.format,
      assetCount: result.assetCount,
    };
  }
  const [css, template, art, manifestText] = await Promise.all([
    readStrictUtf8(source.cssPath, MAX_SKIN_CSS_BYTES),
    fs.readFile(path.join(root, "assets", "renderer-inject.js"), "utf8"),
    fs.readFile(source.artPath),
    readStrictUtf8(source.manifestPath, 64 * 1024),
  ]);
  if (art.length === 0 || art.length > MAX_SKIN_ART_BYTES ||
      art.length < PNG_SIGNATURE.length || !art.subarray(0, PNG_SIGNATURE.length).equals(PNG_SIGNATURE)) {
    throw new Error(`Skin art must be a PNG smaller than ${MAX_SKIN_ART_BYTES} bytes`);
  }
  assertSafeSkinCss(css);
  const manifest = validateSkinManifest(JSON.parse(manifestText), source.expectedId);
  manifest.starlightEnabled = source.starlightEnabled !== false;
  const artDataUrl = `data:image/png;base64,${art.toString("base64")}`;
  const payload = template
    .replace("__DREAM_CSS_JSON__", JSON.stringify(css))
    .replace("__DREAM_ART_JSON__", JSON.stringify(artDataUrl))
    .replace("__DREAM_META_JSON__", JSON.stringify(manifest));
  return { payload, skin: manifest, format: source.format, assetCount: 1 };
}

async function probeSession(session) {
  return session.evaluate(`(() => {
    const markers = {
      shell: Boolean(document.querySelector('main.main-surface')),
      sidebar: Boolean(document.querySelector('aside.app-shell-left-panel')),
      composer: Boolean(document.querySelector('.composer-surface-chrome')),
      main: Boolean(document.querySelector('[role="main"]')),
    };
    return {
      markers,
      codex: location.protocol === 'app:' && markers.shell && markers.sidebar && (markers.composer || markers.main),
    };
  })()`);
}

async function connectTarget(target, port) {
  return new CdpSession(target, port).open();
}

async function connectCodexTargets(port, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const targets = await listAppTargets(port, options.browserId);
      const connected = [];
      for (const target of targets) {
        let session;
        try {
          session = await connectTarget(target, port);
          const probe = await probeSession(session);
          if (probe?.codex) connected.push({ target, session, probe });
          else session.close();
        } catch (error) {
          session?.close();
          lastError = error;
        }
      }
      if (connected.length) return connected;
      lastError = new Error("No page matched the expected Codex shell markers");
    } catch (error) {
      if (error instanceof CdpIdentityMismatchError) throw error;
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 350));
  }
  throw new Error(`No verified Codex renderer on 127.0.0.1:${port}: ${lastError?.message ?? "timed out"}`);
}

async function applyToSession(session, payload) {
  await removeFromSession(session);
  return session.evaluate(payload);
}

async function removeFromSession(session) {
  await session.evaluate(`(() => {
    window.__CODEX_DREAM_SKIN_DISABLED__ = true;
    const state = window.__CODEX_DREAM_SKIN_STATE__;
    if (state?.cleanup) return state.cleanup();
    document.documentElement?.classList.remove('codex-dream-skin');
    document.documentElement?.classList.remove('codex-dream-starlight-off');
    document.documentElement?.style.removeProperty('--dream-art');
    if (document.documentElement) delete document.documentElement.dataset.dreamSkinId;
    document.querySelectorAll('.dream-home').forEach((node) => node.classList.remove('dream-home'));
    document.querySelectorAll('.dream-home-shell').forEach((node) => node.classList.remove('dream-home-shell'));
    document.querySelectorAll('.dream-home-task-mode').forEach((node) => node.classList.remove('dream-home-task-mode'));
    document.querySelectorAll('.dream-task-suggestions').forEach((node) => node.classList.remove('dream-task-suggestions'));
    document.querySelectorAll('.dream-task-suggestion').forEach((node) => node.classList.remove('dream-task-suggestion'));
    document.getElementById('codex-dream-skin-style')?.remove();
    document.getElementById('codex-dream-skin-chrome')?.remove();
    delete window.__CODEX_DREAM_SKIN_STATE__;
    return true;
  })()`);
  return session.evaluate(REMOVE_THEME_V2_EXPRESSION);
}

async function verifyRemovedSession(session) {
  const dreamRemoved = await session.evaluate(`(() =>
    !document.documentElement.classList.contains('codex-dream-skin') &&
    !document.documentElement.classList.contains('codex-dream-starlight-off') &&
    !document.documentElement.style.getPropertyValue('--dream-art') &&
    !document.documentElement.dataset.dreamSkinId &&
    !document.querySelector('.dream-home') &&
    !document.querySelector('.dream-home-shell') &&
    !document.getElementById('codex-dream-skin-style') &&
    !document.getElementById('codex-dream-skin-chrome') &&
    !window.__CODEX_DREAM_SKIN_STATE__
  )()`);
  const themeV2Removed = await session.evaluate(VERIFY_THEME_V2_REMOVED_EXPRESSION);
  return dreamRemoved && themeV2Removed;
}

async function verifySession(session, format) {
  if (format === "awesome-v2") return session.evaluate(themeV2VerifyExpression());
  return session.evaluate(`(() => {
    const box = (node) => {
      if (!node) return null;
      const r = node.getBoundingClientRect();
      return { x: Math.round(r.x), y: Math.round(r.y), width: Math.round(r.width), height: Math.round(r.height) };
    };
    const corners = (node) => {
      if (!node) return null;
      const style = getComputedStyle(node);
      return {
        topLeft: parseFloat(style.borderTopLeftRadius) || 0,
        topRight: parseFloat(style.borderTopRightRadius) || 0,
        bottomRight: parseFloat(style.borderBottomRightRadius) || 0,
        bottomLeft: parseFloat(style.borderBottomLeftRadius) || 0,
      };
    };
    const everyCornerAtLeast = (value, minimum) => value && Object.values(value).every((radius) => radius >= minimum);
    const connectedLeftCorners = (value, minimum) => value &&
      value.topLeft >= minimum && value.bottomLeft >= minimum &&
      value.topRight <= 1 && value.bottomRight <= 1;
    const connectedRightCorners = (value, minimum) => value &&
      value.topRight >= minimum && value.bottomRight >= minimum &&
      value.topLeft <= 1 && value.bottomLeft <= 1;
    const home = document.querySelector('.dream-home');
    const suggestions = home?.querySelector('.group\\\\/home-suggestions') ?? null;
    const cards = suggestions ? [...suggestions.querySelectorAll('button')].map(box) : [];
    const taskContainer = home?.querySelector('.dream-task-suggestions') ?? null;
    const taskCards = taskContainer ? [...taskContainer.querySelectorAll('.dream-task-suggestion')] : [];
    const taskStyle = taskContainer ? getComputedStyle(taskContainer) : null;
    const taskColumns = taskStyle?.gridTemplateColumns === 'none'
      ? 0
      : taskStyle?.gridTemplateColumns.split(/\\s+/).filter(Boolean).length ?? 0;
    const taskDecorationsHidden = taskCards.length === 0 || ['.dream-polaroid', '.dream-ribbon'].every((selector) => {
      const node = document.querySelector(selector);
      return !node || getComputedStyle(node).display === 'none';
    });
    const sidebarNode = document.querySelector('aside.app-shell-left-panel');
    const mainSurfaceNode = document.querySelector('main.main-surface');
    const chromeNode = document.getElementById('codex-dream-skin-chrome');
    const composerNode = document.querySelector('.composer-surface-chrome');
    const windowsMenuNode = document.querySelector('.app-header-tint[class~="group/application-menu-top-bar"]');
    const windowsMenuSeam = (() => {
      if (!windowsMenuNode || !sidebarNode) return { clearance: null, pass: true };
      const boundary = sidebarNode.getBoundingClientRect().right;
      const controls = [...windowsMenuNode.querySelectorAll(':is(button, [role=button])[aria-haspopup="menu"]')]
        .map((node) => node.getBoundingClientRect())
        .filter((rect) => rect.width > 0 && rect.height > 0);
      if (controls.length === 0) return { clearance: null, pass: true };
      const clearance = Math.min(...controls.map((rect) => {
        if (boundary > rect.left && boundary < rect.right)
          return -Math.min(boundary - rect.left, rect.right - boundary);
        return Math.min(Math.abs(boundary - rect.left), Math.abs(boundary - rect.right));
      }));
      return { clearance: Math.round(clearance * 10) / 10, pass: clearance >= 8 };
    })();
    const result = {
      installed: document.documentElement.classList.contains('codex-dream-skin'),
      version: window.__CODEX_DREAM_SKIN_STATE__?.version ?? null,
      skinId: window.__CODEX_DREAM_SKIN_STATE__?.skinId ?? null,
      starlightEnabled: window.__CODEX_DREAM_SKIN_STATE__?.starlightEnabled ?? true,
      documentSkinId: document.documentElement.dataset.dreamSkinId ?? null,
      expectedVersion: ${JSON.stringify(SKIN_VERSION)},
      stylePresent: Boolean(document.getElementById('codex-dream-skin-style')),
      chromePresent: Boolean(document.getElementById('codex-dream-skin-chrome')),
      chromePointerEvents: getComputedStyle(chromeNode || document.body).pointerEvents,
      windowsMenuIntegrated: !windowsMenuNode || windowsMenuNode.classList.contains('dream-windows-menu-bar'),
      windowsMenuSeamClearance: windowsMenuSeam.clearance,
      windowsMenuSeamPass: windowsMenuSeam.pass,
      shellAttached: Boolean(sidebarNode?.classList.contains('dream-shell-attached-main') &&
        mainSurfaceNode?.classList.contains('dream-shell-attached-sidebar')),
      homePresent: Boolean(home),
      suggestionsPresent: Boolean(suggestions),
      hero: box(home?.firstElementChild?.firstElementChild?.firstElementChild),
      cards,
      taskGrid: box(taskContainer),
      taskCards: taskCards.map(box),
      taskColumns,
      taskDecorationsHidden,
      composer: box(composerNode),
      sidebar: box(sidebarNode),
      mainSurface: box(mainSurfaceNode),
      roundedCorners: {
        sidebar: corners(sidebarNode),
        mainSurface: corners(mainSurfaceNode),
        chrome: corners(chromeNode),
        composer: corners(composerNode),
      },
      viewport: { width: innerWidth, height: innerHeight },
      documentOverflow: {
        x: document.documentElement.scrollWidth > document.documentElement.clientWidth,
        y: document.documentElement.scrollHeight > document.documentElement.clientHeight,
      },
    };
    result.taskGap = result.taskCards.length > 0 && result.hero
      ? result.taskCards[0].y - (result.hero.y + result.hero.height)
      : null;
    result.shellSeamPass = !result.shellAttached || Boolean(result.sidebar && result.mainSurface &&
      Math.abs(result.sidebar.x + result.sidebar.width - result.mainSurface.x) <= 2 &&
      Math.abs(result.sidebar.y - result.mainSurface.y) <= 2);
    result.roundedShellPass = (result.shellAttached
      ? connectedLeftCorners(result.roundedCorners.sidebar, 18) &&
        connectedRightCorners(result.roundedCorners.mainSurface, 18) &&
        connectedRightCorners(result.roundedCorners.chrome, 18)
      : everyCornerAtLeast(result.roundedCorners.sidebar, 18) &&
        everyCornerAtLeast(result.roundedCorners.mainSurface, 18) &&
        everyCornerAtLeast(result.roundedCorners.chrome, 18)) &&
      everyCornerAtLeast(result.roundedCorners.composer, 20);
    result.pass = result.installed && result.version === result.expectedVersion &&
      Boolean(result.skinId) && result.skinId === result.documentSkinId &&
      result.stylePresent && result.chromePresent && result.windowsMenuIntegrated && result.windowsMenuSeamPass && result.shellSeamPass &&
      result.chromePointerEvents === 'none' && Boolean(result.composer) && Boolean(result.sidebar) && result.roundedShellPass &&
      (!result.homePresent || (Boolean(result.hero) &&
        (!result.suggestionsPresent || (result.cards.length >= 2 && result.cards.length <= 4)))) &&
      (result.taskCards.length === 0 || (result.taskColumns >= 2 &&
        result.taskCards.every((card) => card.height <= 112) && result.taskDecorationsHidden &&
        result.taskGap >= 12));
    return result;
  })()`);
}

async function waitForVerifiedSession(session, timeoutMs, format) {
  const deadline = Date.now() + timeoutMs;
  let lastResult;
  let lastError;
  while (Date.now() < deadline) {
    try {
      lastResult = await verifySession(session, format);
      lastError = null;
      if (lastResult.pass) return lastResult;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  if (!lastResult && lastError) throw lastError;
  return lastResult;
}

async function capture(session, outputPath) {
  await fs.mkdir(path.dirname(outputPath), { recursive: true });
  await session.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Escape", code: "Escape", windowsVirtualKeyCode: 27 });
  await session.send("Input.dispatchKeyEvent", { type: "keyUp", key: "Escape", code: "Escape", windowsVirtualKeyCode: 27 });
  const viewport = await session.evaluate("({ width: innerWidth, height: innerHeight })");
  await session.send("Input.dispatchMouseEvent", {
    type: "mouseMoved",
    x: Math.round(viewport.width * 0.64),
    y: Math.round(viewport.height * 0.62),
    button: "none",
  });
  await new Promise((resolve) => setTimeout(resolve, 300));
  const result = await session.send("Page.captureScreenshot", {
    format: "png",
    fromSurface: true,
    captureBeyondViewport: false,
  });
  await fs.writeFile(outputPath, Buffer.from(result.data, "base64"));
}

async function runOneShot(options) {
  const connected = await connectCodexTargets(options.port, options.timeoutMs);
  const selectedSource = options.mode === "remove" ? null : await loadSkinSource(options);
  const loaded = (options.mode === "once" || options.reload) ? await loadPayload(options) : null;
  const payload = loaded?.payload ?? null;
  const format = loaded?.format ?? selectedSource?.format ?? "dream-v1";
  const results = [];
  let screenshotCaptured = false;
  try {
    for (const { target, session, probe } of connected) {
      try {
        if (options.mode === "remove") await removeFromSession(session);
        else if (options.mode === "once") await applyToSession(session, payload);
        if (options.mode === "once") {
          await new Promise((resolve) => setTimeout(resolve, 850));
        }
        if (options.reload) {
          await session.send("Page.reload", { ignoreCache: true });
          await new Promise((resolve) => setTimeout(resolve, 1600));
          if (options.mode !== "remove") await applyToSession(session, payload);
        }
        const verified = options.mode === "remove"
          ? await verifyRemovedSession(session)
          : (options.reload || options.mode === "once" || options.mode === "verify")
            ? await waitForVerifiedSession(session, options.timeoutMs, format)
            : await verifySession(session, format);
        results.push({ targetId: target.id, markers: probe.markers, result: verified });
        if (options.screenshot && !screenshotCaptured) {
          await capture(session, options.screenshot);
          screenshotCaptured = true;
        }
      } finally {
        session.close();
      }
    }
  } finally {
    for (const { session } of connected) session.close();
  }
  console.log(JSON.stringify({ mode: options.mode, port: options.port, targets: results }, null, 2));
  const failed = results.length === 0 || results.some((item) =>
    options.mode === "remove" ? item.result !== true : !item.result?.pass);
  if (failed) process.exitCode = 2;
}

async function runWatch(options) {
  const identityAnchor = await connectBrowserIdentityAnchor(options.port, options.browserId);
  const sessions = new Map();
  const targetFailures = new Map();
  let stopping = false;
  let listFailures = 0;
  let lastListErrorLogAt = 0;
  const stop = () => { stopping = true; };
  const rejectTarget = (target, baseDelayMs, error = null) => {
    const previous = targetFailures.get(target.id) ?? { failures: 0, lastLogAt: 0 };
    const failures = previous.failures + 1;
    const delayMs = Math.min(30000, baseDelayMs * (2 ** Math.min(failures - 1, 4)));
    const now = Date.now();
    if (error && (failures === 1 || now - previous.lastLogAt >= 30000)) {
      console.error(`[dream-skin] inject failed for ${target.id}: ${error.message}; retrying in ${delayMs}ms`);
      previous.lastLogAt = now;
    }
    targetFailures.set(target.id, { failures, lastLogAt: previous.lastLogAt, until: now + delayMs });
  };
  process.on("SIGINT", stop);
  process.on("SIGTERM", stop);

  try {
    const { payload } = await loadPayload(options);
    while (!stopping) {
      if (identityAnchor.closed) {
        console.error("[dream-skin] original CDP browser identity closed; watcher is stopping instead of reconnecting");
        process.exitCode = 3;
        break;
      }
      let targets = [];
      try {
        targets = await listAppTargets(options.port);
        listFailures = 0;
      } catch (error) {
        listFailures += 1;
        const retryMs = Math.min(10000, 1000 * (2 ** Math.min(listFailures - 1, 4)));
        if (listFailures === 1 || Date.now() - lastListErrorLogAt >= 30000) {
          console.error(`[dream-skin] ${new Date().toISOString()} ${error.message}; retrying in ${retryMs}ms`);
          lastListErrorLogAt = Date.now();
        }
        await new Promise((resolve) => setTimeout(resolve, retryMs));
        continue;
      }

      const activeIds = new Set(targets.map((target) => target.id));
      for (const id of targetFailures.keys()) {
        if (!activeIds.has(id)) targetFailures.delete(id);
      }
      for (const [id, session] of sessions) {
        if (!activeIds.has(id) || session.closed) {
          session.close();
          sessions.delete(id);
          targetFailures.delete(id);
        }
      }

      for (const target of targets) {
        if (identityAnchor.closed) break;
        if (sessions.has(target.id)) continue;
        if ((targetFailures.get(target.id)?.until ?? 0) > Date.now()) continue;
        let session;
        try {
          session = await connectTarget(target, options.port);
          if (identityAnchor.closed) throw new CdpIdentityMismatchError("Original CDP browser identity closed");
          const probe = await probeSession(session);
          if (!probe?.codex) {
            rejectTarget(target, 5000);
            session.close();
            continue;
          }
          let lastReinjectErrorLogAt = 0;
          session.on("Page.loadEventFired", () => {
            setTimeout(() => applyToSession(session, payload).catch((error) => {
              if (Date.now() - lastReinjectErrorLogAt >= 30000) {
                console.error(`[dream-skin] reinject failed for ${target.id}: ${error.message}`);
                lastReinjectErrorLogAt = Date.now();
              }
            }), 250);
          });
          if (identityAnchor.closed) throw new CdpIdentityMismatchError("Original CDP browser identity closed");
          await applyToSession(session, payload);
          sessions.set(target.id, session);
          targetFailures.delete(target.id);
          console.log(`[dream-skin] injected target ${target.id}`);
        } catch (error) {
          session?.close();
          if (identityAnchor.closed || error instanceof CdpIdentityMismatchError) break;
          rejectTarget(target, 2500, error);
        }
      }
      await new Promise((resolve) => setTimeout(resolve, 1200));
    }
  } finally {
    identityAnchor.close();
    for (const session of sessions.values()) session.close();
  }
}

const options = parseArgs(process.argv.slice(2));
if (options.mode === "self-test") {
  const valid = validatedDebuggerUrl({ webSocketDebuggerUrl: `ws://127.0.0.1:${options.port}/devtools/page/test` }, options.port);
  const browserId = browserIdFromVersion({
    webSocketDebuggerUrl: `ws://127.0.0.1:${options.port}/devtools/browser/test-browser`,
  }, options.port);
  const invalid = [
    "ws://example.com/devtools/page/test",
    `ws://127.0.0.1:${options.port + 1}/devtools/page/test`,
    `wss://127.0.0.1:${options.port}/devtools/page/test`,
    `ws://user@127.0.0.1:${options.port}/devtools/page/test`,
    `ws://127.0.0.1:${options.port}/unexpected/test`,
    `ws://127.0.0.1:${options.port}/devtools/page/test?query=1`,
  ];
  for (const value of invalid) {
    let rejected = false;
    try { validatedDebuggerUrl({ webSocketDebuggerUrl: value }, options.port); } catch { rejected = true; }
    if (!rejected) throw new Error(`CDP URL validation accepted an unsafe URL: ${value}`);
  }
  const invalidBrowserUrls = [
    `ws://127.0.0.1:${options.port}/devtools/page/not-a-browser`,
    `ws://127.0.0.1:${options.port}/devtools/browser/bad%20id`,
    `ws://127.0.0.1:${options.port}/devtools/browser/test?query=1`,
  ];
  for (const value of invalidBrowserUrls) {
    let rejected = false;
    try { browserIdFromVersion({ webSocketDebuggerUrl: value }, options.port); } catch { rejected = true; }
    if (!rejected) throw new Error(`Browser identity validation accepted an unsafe URL: ${value}`);
  }
  const validPageTarget = {
    id: "page-test",
    type: "page",
    url: "app://codex/",
    webSocketDebuggerUrl: `ws://127.0.0.1:${options.port}/devtools/page/page-test`,
  };
  const invalidPageTargets = [
    { ...validPageTarget, webSocketDebuggerUrl: `ws://127.0.0.1:${options.port}/devtools/browser/page-test` },
    { ...validPageTarget, id: "other-page" },
    { ...validPageTarget, id: 123 },
    { ...validPageTarget, type: "other" },
  ];
  if (!valid || browserId !== "test-browser" || !isValidCdpPageTarget(validPageTarget, options.port) ||
      invalidPageTargets.some((item) => isValidCdpPageTarget(item, options.port))) {
    throw new Error("CDP URL and target validation self-test failed");
  }
  console.log(JSON.stringify({ pass: true, version: SKIN_VERSION, test: "loopback-cdp-validation" }));
} else if (options.mode === "check-payload") {
  const { payload, skin, format, assetCount } = await loadPayload(options);
  const unresolved = format === "awesome-v2"
    ? ["__CTS_CSS_JSON__", "__CTS_THEME_JSON__", "__CTS_CHROME_JSON__", "__CTS_MOTION_JSON__", "__CTS_STARLIGHT_JSON__"]
    : ["__DREAM_CSS_JSON__", "__DREAM_ART_JSON__", "__DREAM_META_JSON__"];
  if (unresolved.some((marker) => payload.includes(marker))) throw new Error("Payload placeholders were not fully replaced");
  console.log(JSON.stringify({ pass: true, version: SKIN_VERSION, skinId: skin.id,
    starlightEnabled: skin.starlightEnabled !== false, format, assetCount,
    payloadBytes: Buffer.byteLength(payload) }));
} else if (options.mode === "watch") await runWatch(options);
else await runOneShot(options);
