// Renderer-side runtime. Injected via Runtime.evaluate — must be idempotent,
// re-entrant and fully reversible. Placeholders are substituted by payload.mjs.
//
// Flicker discipline: ensure() runs on every DOM mutation, so EVERY write in
// here must be guarded by a value comparison — writing the same value to
// style/class/attributes still dirties style state in Chromium and causes
// visible repaint flashes (e.g. whenever a dropdown portal mounts).
((cssText, themeConfig, chromeHtml, motionAssets, starlightEnabled) => {
  const STATE_KEY = "__CODEX_THEME_STUDIO__";
  const DISABLED_KEY = "__CODEX_THEME_STUDIO_DISABLED__";
  const STYLE_ID = "cts-style";
  const CHROME_ID = "cts-chrome";
  const STAGE_ID = "cts-stage";
  const INTRO_ID = "cts-intro";
  const STARLIGHT_ID = "cts-starlight";
  const ROOT_CLASS = "codex-theme-studio";
  const THEME_ATTR = "data-cts-theme";
  const SHELL_ATTR = "data-cts-shell";
  const MOTION_ATTR = "data-cts-motion";
  const WINDOWS_MENU_CLASS = "cts-windows-menu-bar";
  const WINDOWS_MENU_REGION_ATTR = "data-cts-menu-region";
  const WINDOWS_MENU_COMMAND_GROUP_ATTR = "data-cts-menu-command-group";
  const ATTACHED_MAIN_CLASS = "cts-shell-attached-main";
  const ATTACHED_SIDEBAR_CLASS = "cts-shell-attached-sidebar";
  const COMPOSER_OVERFLOW_ATTR = "data-cts-composer-overflow";
  const COMPOSER_MODE_ATTR = "data-cts-composer-mode";
  const createComposerOverflowAnnotator = __CTS_CREATE_COMPOSER_OVERFLOW_ANNOTATOR__;
  const selectComposerSurfaces = __CTS_SELECT_COMPOSER_SURFACES__;
  const RUNTIME_CSS = `
html.codex-theme-studio .cts-windows-menu-bar {
  position: absolute !important;
  inset: 0 0 auto 0 !important;
  height: var(--cts-windows-menu-height, 36px) !important;
}
html.codex-theme-studio .cts-windows-menu-bar + * > aside.app-shell-left-panel {
  padding-top: calc(var(--cts-windows-menu-height, 36px) + var(--cts-windows-sidebar-padding-top, 0px)) !important;
}
html.codex-theme-studio .cts-windows-menu-bar + * > main.main-surface {
  padding-top: calc(var(--cts-windows-menu-height, 36px) + var(--cts-windows-main-padding-top, 0px)) !important;
}
html.codex-theme-studio .cts-windows-menu-bar [data-cts-menu-region="sidebar"] {
  color: var(--cts-windows-sidebar-foreground) !important;
  -webkit-text-fill-color: var(--cts-windows-sidebar-foreground) !important;
}
html.codex-theme-studio .cts-windows-menu-bar [data-cts-menu-region="main"] {
  color: var(--cts-windows-main-foreground) !important;
  -webkit-text-fill-color: var(--cts-windows-main-foreground) !important;
}
html.codex-theme-studio .cts-windows-menu-bar [data-cts-menu-command-group] {
  transform: translate3d(var(--cts-windows-menu-command-offset, 0px), 0, 0) !important;
}
html.codex-theme-studio .cts-windows-menu-bar [data-cts-menu-command-group] :is(button, [role=button])[aria-haspopup="menu"] {
  padding-left: var(--cts-windows-menu-inline-padding, 10px) !important;
  padding-right: var(--cts-windows-menu-inline-padding, 10px) !important;
}
html.codex-theme-studio aside.app-shell-left-panel.cts-shell-attached-main {
  border-top-right-radius: 0 !important;
  border-bottom-right-radius: 0 !important;
}
html.codex-theme-studio main.main-surface.cts-shell-attached-sidebar,
html.codex-theme-studio main.main-surface.cts-shell-attached-sidebar > header.app-header-tint,
html.codex-theme-studio #cts-chrome.cts-shell-attached-sidebar {
  border-top-left-radius: 0 !important;
  border-bottom-left-radius: 0 !important;
}
html.codex-theme-studio aside.app-shell-left-panel [class~="animate-spin"]:has(> svg[class~="icon-xs"]) {
  color: var(--cts-starlight-primary, #8ccaff) !important;
  opacity: .78 !important;
  animation-duration: 1.65s !important;
}
html.codex-theme-studio aside.app-shell-left-panel [class~="animate-spin"]:has(> svg[class~="icon-xs"]) > svg {
  width: 12px !important;
  height: 12px !important;
}
html.codex-theme-studio aside.app-shell-left-panel [class~="animate-spin"]:has(> svg[class~="icon-xs"]) > svg path[opacity] {
  opacity: .16 !important;
}
html.codex-theme-studio #cts-starlight {
  position: absolute;
  inset: 0;
  z-index: 0;
  overflow: hidden;
  pointer-events: none;
  contain: strict;
  color: var(--cts-starlight-primary, #8ccaff);
}
html.codex-theme-studio #cts-starlight > i {
  --cts-star-scale: 1;
  position: absolute;
  left: var(--cts-star-x);
  top: var(--cts-star-y);
  width: 3px;
  height: 3px;
  border-radius: 50%;
  background: currentColor;
  opacity: .22;
  transform: translate3d(0, 8px, 0) scale(var(--cts-star-scale));
  animation: cts-starlight-twinkle var(--cts-star-speed) cubic-bezier(.22, 1, .36, 1) var(--cts-star-delay) infinite both;
  will-change: transform, opacity;
}
html.codex-theme-studio #cts-starlight > i:nth-child(3n) {
  color: var(--cts-starlight-secondary, #e5c5ff);
}
html.codex-theme-studio #cts-starlight > i::before,
html.codex-theme-studio #cts-starlight > i::after {
  content: "";
  position: absolute;
  left: 50%;
  top: 50%;
  border-radius: 999px;
  background: currentColor;
  transform: translate(-50%, -50%);
  opacity: .76;
}
html.codex-theme-studio #cts-starlight > i::before { width: 1px; height: 13px; }
html.codex-theme-studio #cts-starlight > i::after { width: 13px; height: 1px; }
html.codex-theme-studio[data-cts-motion="off"] #cts-stage *,
html.codex-theme-studio[data-cts-motion="off"] #cts-stage *::before,
html.codex-theme-studio[data-cts-motion="off"] #cts-stage *::after,
html.codex-theme-studio[data-cts-motion="off"] #cts-chrome *,
html.codex-theme-studio[data-cts-motion="off"] #cts-chrome *::before,
html.codex-theme-studio[data-cts-motion="off"] #cts-chrome *::after {
  animation: none !important;
  transition: none !important;
}
html.codex-theme-studio[data-cts-motion="paused"] #cts-stage *,
html.codex-theme-studio[data-cts-motion="paused"] #cts-stage *::before,
html.codex-theme-studio[data-cts-motion="paused"] #cts-stage *::after,
html.codex-theme-studio[data-cts-motion="paused"] #cts-chrome *,
html.codex-theme-studio[data-cts-motion="paused"] #cts-chrome *::before,
html.codex-theme-studio[data-cts-motion="paused"] #cts-chrome *::after {
  animation-play-state: paused !important;
}
@keyframes cts-starlight-twinkle {
  0%, 100% { opacity: .08; transform: translate3d(0, 9px, 0) scale(.55); }
  46% { opacity: .78; transform: translate3d(0, 0, 0) scale(var(--cts-star-scale)); }
  68% { opacity: .34; transform: translate3d(3px, -5px, 0) scale(.72); }
}
@media (prefers-reduced-motion: reduce) {
  html.codex-theme-studio aside.app-shell-left-panel [class~="animate-spin"]:has(> svg[class~="icon-xs"]) {
    animation: none !important;
  }
  html.codex-theme-studio #cts-starlight { display: none !important; }
  html.codex-theme-studio #cts-starlight > i { animation: none !important; }
}`;
  const VERSION = __CTS_VERSION_JSON__;
  const STAMP = __CTS_STAMP_JSON__;
  const MOTION = motionAssets && typeof motionAssets === "object" ? motionAssets : {};
  const THEME = themeConfig && typeof themeConfig === "object" ? themeConfig : {};
  const STARLIGHT_ENABLED = starlightEnabled !== false;

  window[DISABLED_KEY] = false;

  // Tear down any previous install (idempotent re-entry, incl. theme switch).
  const previous = window[STATE_KEY];
  if (previous?.observer) previous.observer.disconnect();
  if (previous?.timer) clearInterval(previous.timer);
  if (previous?.clock) clearInterval(previous.clock);
  if (previous?.scheduler?.timeout) clearTimeout(previous.scheduler.timeout);
  if (previous?.resizeHandler) window.removeEventListener("resize", previous.resizeHandler);
  if (previous?.visibilityHandler) document.removeEventListener("visibilitychange", previous.visibilityHandler);
  if (previous?.mediaHandler && previous?.mediaQuery) {
    try { previous.mediaQuery.removeEventListener("change", previous.mediaHandler); } catch {}
  }
  // Disable the old menu rule before its variables are cleared so the first
  // pass of a hot-switched theme measures the shell's real base padding.
  document.querySelectorAll(`.${WINDOWS_MENU_CLASS}`)
    .forEach((node) => node.classList.remove(WINDOWS_MENU_CLASS));
  document.querySelectorAll(`[${WINDOWS_MENU_REGION_ATTR}]`)
    .forEach((node) => node.removeAttribute(WINDOWS_MENU_REGION_ATTR));
  document.querySelectorAll(`.${ATTACHED_MAIN_CLASS}, .${ATTACHED_SIDEBAR_CLASS}`)
    .forEach((node) => node.classList.remove(ATTACHED_MAIN_CLASS, ATTACHED_SIDEBAR_CLASS));
  if (previous?.appliedVars) {
    for (const name of previous.appliedVars) document.documentElement?.style.removeProperty(name);
  }
  // A different stamp means a different theme (or payload): a still-playing
  // intro from the previous theme must not outlive it, and its stale node
  // would also make the new theme's playIntro() bail out. Same-stamp
  // re-ensures leave the intro alone — reconciliation must never cut it.
  if (previous && previous.stamp !== STAMP) document.getElementById(INTRO_ID)?.remove();
  document.querySelectorAll(`[${COMPOSER_OVERFLOW_ATTR}]`)
    .forEach((node) => node.removeAttribute(COMPOSER_OVERFLOW_ATTR));
  document.querySelectorAll(`[${COMPOSER_MODE_ATTR}]`)
    .forEach((node) => node.removeAttribute(COMPOSER_MODE_ATTR));

  // Split the chrome fragment into its layers: "overlay" floats above the UI
  // (fixed, z31), "stage" is scenery mounted inside main UNDER the content.
  // Fragments without layer markers keep the legacy all-overlay behaviour.
  const layers = (() => {
    const tpl = document.createElement("template");
    tpl.innerHTML = chromeHtml || "";
    const overlay = tpl.content.querySelector('[data-cts-layer="overlay"]');
    const stage = tpl.content.querySelector('[data-cts-layer="stage"]');
    return {
      overlayHtml: overlay ? overlay.innerHTML : (stage ? "" : (chromeHtml || "")),
      stageHtml: stage ? stage.innerHTML : "",
    };
  })();

  const appliedVars = [];
  const setVar = (name, value) => {
    const root = document.documentElement;
    if (root.style.getPropertyValue(name) !== value) root.style.setProperty(name, value);
    if (!appliedVars.includes(name)) appliedVars.push(name);
  };

  const setAttr = (node, name, value) => {
    if (node.getAttribute(name) !== value) node.setAttribute(name, value);
  };

  const setClass = (node, name, on) => {
    if (node.classList.contains(name) !== on) node.classList.toggle(name, on);
  };

  const pickThemeColor = (keys, fallback) => {
    const colors = THEME.colors && typeof THEME.colors === "object" ? THEME.colors : {};
    for (const key of keys) {
      const value = colors[key];
      if (typeof value === "string" && value.trim()) return value;
    }
    return fallback;
  };

  const setStarlightPalette = (shellMode) => {
    const themeMode = THEME.codexTheme?.[shellMode];
    const accent = typeof themeMode?.accent === "string" ? themeMode.accent : null;
    setVar("--cts-starlight-primary", accent || pickThemeColor(
      ["cyan", "violet", "gold", "amber", "rose", "pearl", "silver"], "#8ccaff"));
    setVar("--cts-starlight-secondary", pickThemeColor(
      ["pearl", "silver", "cyan", "rose", "gold", "amber", "violet"], accent || "#e5c5ff"));
  };

  const ensureStarlight = (stage) => {
    let field = document.getElementById(STARLIGHT_ID);
    if (!STARLIGHT_ENABLED || !stage) {
      field?.remove();
      return;
    }
    if (!field || field.parentElement !== stage) {
      field?.remove();
      field = document.createElement("div");
      field.id = STARLIGHT_ID;
      field.setAttribute("aria-hidden", "true");
      const stars = [
        [8, 18, 1.00, 4.8, -1.1], [18, 71, .72, 5.6, -3.7], [29, 34, .84, 4.2, -2.2],
        [39, 84, .62, 6.1, -4.4], [51, 13, .92, 5.3, -2.9], [61, 62, .70, 4.6, -.8],
        [70, 27, .78, 5.9, -3.2], [78, 78, 1.05, 4.9, -1.7], [88, 42, .68, 6.3, -5.1],
        [94, 17, .82, 5.1, -2.5], [13, 48, .60, 6.5, -4.8], [84, 58, .58, 5.7, -3.9],
      ];
      field.innerHTML = stars.map(([x, y, scale, speed, delay]) =>
        `<i style="--cts-star-x:${x}%;--cts-star-y:${y}%;--cts-star-scale:${scale};--cts-star-speed:${speed}s;--cts-star-delay:${delay}s"></i>`
      ).join("");
      stage.prepend(field);
    }
  };

  const detectShellMode = () => {
    const root = document.documentElement;
    const cls = `${root.className || ""} ${document.body?.className || ""}`.toLowerCase();
    if (/\b(dark|theme-dark|appearance-dark)\b/.test(cls)) return "dark";
    if (/\b(light|theme-light|appearance-light)\b/.test(cls)) return "light";
    const dataTheme = (
      root.getAttribute("data-theme") || root.getAttribute("data-appearance") ||
      root.getAttribute("data-color-mode") || document.body?.getAttribute("data-theme") || ""
    ).toLowerCase();
    if (dataTheme.includes("dark")) return "dark";
    if (dataTheme.includes("light")) return "light";
    try {
      if (window.matchMedia("(prefers-color-scheme: dark)").matches) return "dark";
    } catch {}
    return "light";
  };

  // Sticky route detection: only flip home-state on positive signals, so
  // transient DOM (dropdown portals, dialogs) never toggles theme classes.
  const findHome = (sticky) => {
    const indicator = document.querySelector('[data-testid="home-icon"]');
    if (indicator) return indicator.closest('[role="main"]');
    const bySuggestions = [...document.querySelectorAll('[role="main"]')]
      .find((candidate) => candidate.querySelector('.group\\/home-suggestions'));
    if (bySuggestions) return bySuggestions;
    if (sticky?.isConnected) return sticky; // keep last known while it lives
    return null;
  };

  const chromeRectCache = { left: NaN, top: NaN, width: NaN, height: NaN };

  // Semantic icon annotation: CSS cannot match by text, so tag well-known
  // controls with data-cts-icon and let theme CSS attach bitmap icons.
  // Idempotent — tagged nodes are skipped, and the attribute is not in the
  // observer's attributeFilter, so tagging never re-triggers ensure().
  const SIDEBAR_ICONS = [
    { icon: "new-task", texts: ["新建任务", "New task"] },
    { icon: "scheduled", texts: ["已安排", "Scheduled"] },
    { icon: "plugins", texts: ["插件", "Plugins"] },
    { icon: "sites", texts: ["站点", "Sites"] },
    { icon: "pull-request", texts: ["拉取请求", "Pull request"] },
    { icon: "chat", texts: ["聊天", "Chat"] },
  ];
  const CARD_ICONS = ["explore", "build", "review", "fix"];

  // The glyph attribute lands on the FIRST svg inside the control, so sibling
  // svgs (dropdown chevrons etc.) keep their native artwork.
  const tagGlyph = (container, icon) => {
    if (!container || container.dataset.ctsIcon) return;
    const svg = container.querySelector("svg");
    if (!svg) return;
    container.dataset.ctsIcon = icon;
    svg.dataset.ctsGlyph = icon;
  };

  // Codex 26.715.31251 introduced a scrollable composer shell, text lane and
  // finite-height editor root. Later builds can switch the same nodes between
  // single-line and multiline layouts. The shared helper measures native
  // capabilities without letting our own hardening roles contaminate them.
  const annotateComposerOverflow = createComposerOverflowAnnotator({
    overflowAttribute: COMPOSER_OVERFLOW_ATTR,
    modeAttribute: COMPOSER_MODE_ATTR,
    readStyle: (node) => getComputedStyle(node),
    viewportSignature: () => `${innerWidth}x${innerHeight}`,
  });

  const annotateIcons = () => {
    const aside = document.querySelector(".app-shell-left-panel");
    if (aside) {
      for (const button of aside.querySelectorAll("button:not([data-cts-icon])")) {
        const text = button.textContent || "";
        const rule = SIDEBAR_ICONS.find((entry) => entry.texts.some((t) => text.includes(t)));
        if (rule) tagGlyph(button, rule.icon);
      }
      const search = aside.querySelector('[aria-label="搜索"]:not([data-cts-icon]), [aria-label="Search"]:not([data-cts-icon])');
      if (search) tagGlyph(search, "search");
      // Workspace title → tokusatsu logo. The title text is split across
      // child spans ("ChatGPT" + "工作"), so match on the whole button and
      // re-evaluate every pass (the same button swaps text on switch).
      for (const button of aside.querySelectorAll("button")) {
        const text = button.textContent.replace(/\s+/g, " ").trim();
        const isCodex = text === "Codex";
        const isWork = /^ChatGPT ?(工作|Work)$/i.test(text);
        if (!isCodex && !isWork) {
          if (button.dataset.ctsLogo) delete button.dataset.ctsLogo;
          continue;
        }
        const want = isCodex ? "codex" : "chatgpt-work";
        if (button.dataset.ctsLogo !== want) button.dataset.ctsLogo = want;
      }
    }
    const composer = document.querySelector(".composer-surface-chrome");
    if (composer) {
      for (const button of composer.querySelectorAll("button:not([data-cts-icon])")) {
        const aria = button.getAttribute("aria-label") || "";
        const text = button.textContent || "";
        if (aria.includes("添加文件") || aria.toLowerCase().includes("add file")) tagGlyph(button, "attach");
        else if (aria.includes("听写") || /dictat/i.test(aria)) tagGlyph(button, "mic");
        else if (button.querySelector("svg") && /sol|spark|codex|gpt/i.test(text)) tagGlyph(button, "model");
      }
    }
    document.querySelectorAll('.cts-home .group\\/home-suggestions .grid > div').forEach((cell, index) => {
      const button = cell.querySelector("button:not([data-cts-icon])");
      if (button && CARD_ICONS[index]) tagGlyph(button, CARD_ICONS[index]);
    });
  };

  // Codex 26.715+ renders the Windows application menu (File/Edit/View/Help)
  // as a separate 36px flex item above the sidebar/main row. Theme CSS written
  // for the older in-main toolbar cannot reach that strip, so the stock canvas
  // shows through. Move only this structurally verified menu out of flex flow,
  // then use equivalent top padding on the real sidebar/main surfaces: their
  // own theme backgrounds extend behind the menu without cloning per-theme
  // artwork or changing any content geometry.
  const integrateWindowsMenu = (shellMain) => {
    const menu = document.querySelector(
      '.app-header-tint[class~="group/application-menu-top-bar"]'
    );
    const shellRow = menu?.nextElementSibling;
    const sidebar = shellRow?.querySelector(":scope > aside.app-shell-left-panel");
    const main = shellRow?.querySelector(":scope > main.main-surface");
    const menuBox = menu?.getBoundingClientRect();
    const integrated = Boolean(menu?.classList.contains(WINDOWS_MENU_CLASS));
    const eligible = Boolean(
      menu && sidebar && main && main === shellMain &&
      menuBox && menuBox.width > 0 && menuBox.height > 0
    );

    for (const stale of document.querySelectorAll(`.${WINDOWS_MENU_CLASS}`)) {
      if (!eligible || stale !== menu) stale.classList.remove(WINDOWS_MENU_CLASS);
    }
    for (const stale of document.querySelectorAll(`[${WINDOWS_MENU_REGION_ATTR}]`)) {
      if (!eligible || !menu.contains(stale)) stale.removeAttribute(WINDOWS_MENU_REGION_ATTR);
    }
    for (const stale of document.querySelectorAll(`[${WINDOWS_MENU_COMMAND_GROUP_ATTR}]`)) {
      if (!eligible || !menu.contains(stale)) stale.removeAttribute(WINDOWS_MENU_COMMAND_GROUP_ATTR);
    }
    if (!eligible) return;

    const sidebarStyle = getComputedStyle(sidebar);
    const mainStyle = getComputedStyle(main);
    const appliedOffset = integrated ? menuBox.height : 0;
    const basePadding = (style) =>
      `${Math.max(0, (Number.parseFloat(style.paddingTop) || 0) - appliedOffset)}px`;
    setVar("--cts-windows-menu-height", `${menuBox.height}px`);
    setVar("--cts-windows-sidebar-padding-top", basePadding(sidebarStyle));
    setVar("--cts-windows-main-padding-top", basePadding(mainStyle));
    setClass(menu, WINDOWS_MENU_CLASS, true);
    setVar("--cts-windows-sidebar-foreground", sidebarStyle.color);
    setVar("--cts-windows-main-foreground", mainStyle.color);

    const sidebarRight = sidebar.getBoundingClientRect().right;
    const commandControls = [...menu.querySelectorAll(':is(button, [role=button])[aria-haspopup="menu"]')]
      .filter((control) => {
        const box = control.getBoundingClientRect();
        return box.width > 0 && box.height > 0;
      });
    const commandGroup = commandControls[0]?.parentElement ?? null;
    for (const stale of document.querySelectorAll(`[${WINDOWS_MENU_COMMAND_GROUP_ATTR}]`)) {
      if (stale !== commandGroup) stale.removeAttribute(WINDOWS_MENU_COMMAND_GROUP_ATTR);
    }
    if (commandGroup && commandControls.every((control) => commandGroup.contains(control))) {
      setAttr(commandGroup, WINDOWS_MENU_COMMAND_GROUP_ATTR, "true");
      const root = document.documentElement;
      const currentOffset = Number.parseFloat(root.style.getPropertyValue("--cts-windows-menu-command-offset")) || 0;
      const metrics = commandControls.map((control) => {
        const box = control.getBoundingClientRect();
        const style = getComputedStyle(control);
        return {
          box,
          intrinsic: Math.max(0, box.width - (Number.parseFloat(style.paddingLeft) || 0) -
            (Number.parseFloat(style.paddingRight) || 0)),
        };
      });
      const firstLeft = metrics[0].box.left - currentOffset;
      const totalWidth = metrics.reduce((sum, metric) => sum + metric.box.width, 0);
      const intrinsicWidth = metrics.reduce((sum, metric) => sum + metric.intrinsic, 0);
      const interItemGap = Math.max(0,
        metrics[metrics.length - 1].box.right - metrics[0].box.left - totalWidth);
      const availablePadding = (sidebarRight - 8 - firstLeft - interItemGap - intrinsicWidth) /
        (metrics.length * 2);
      if (availablePadding >= 5) {
        const padding = Math.floor(Math.min(10, availablePadding) * 2) / 2;
        setVar("--cts-windows-menu-inline-padding", `${padding}px`);
        setVar("--cts-windows-menu-command-offset", "0px");
      } else {
        setVar("--cts-windows-menu-inline-padding", "10px");
        setVar("--cts-windows-menu-command-offset",
          `${Math.max(0, Math.ceil(sidebarRight + 8 - firstLeft))}px`);
      }
    } else {
      setVar("--cts-windows-menu-inline-padding", "10px");
      setVar("--cts-windows-menu-command-offset", "0px");
    }
    for (const control of menu.querySelectorAll("button, [role=button]")) {
      const box = control.getBoundingClientRect();
      const region = box.left + box.width / 2 <= sidebarRight ? "sidebar" : "main";
      setAttr(control, WINDOWS_MENU_REGION_ATTR, region);
    }
  };

  const integrateShellSeam = (shellMain) => {
    const shellRow = shellMain?.parentElement;
    const sidebar = shellRow?.querySelector(":scope > aside.app-shell-left-panel");
    const main = shellRow?.querySelector(":scope > main.main-surface");
    const eligible = Boolean(sidebar && main && main === shellMain);

    for (const stale of document.querySelectorAll(`.${ATTACHED_MAIN_CLASS}`)) {
      if (!eligible || stale !== sidebar) stale.classList.remove(ATTACHED_MAIN_CLASS);
    }
    for (const stale of document.querySelectorAll(`main.${ATTACHED_SIDEBAR_CLASS}`)) {
      if (!eligible || stale !== main) stale.classList.remove(ATTACHED_SIDEBAR_CLASS);
    }
    if (!eligible) return false;

    const sidebarBox = sidebar.getBoundingClientRect();
    const mainBox = main.getBoundingClientRect();
    const attached = sidebarBox.width > 1 && mainBox.width > 1 &&
      Math.abs(sidebarBox.right - mainBox.left) <= 2 &&
      Math.abs(sidebarBox.top - mainBox.top) <= 2;
    setClass(sidebar, ATTACHED_MAIN_CLASS, attached);
    setClass(main, ATTACHED_SIDEBAR_CLASS, attached);
    return attached;
  };

  const ensure = () => {
    if (window[DISABLED_KEY]) return;
    const root = document.documentElement;
    if (!root || !document.body) return;
    const state = window[STATE_KEY];

    setClass(root, ROOT_CLASS, true);
    setAttr(root, THEME_ATTR, THEME.id || "custom");
    const shellMode = detectShellMode();
    setAttr(root, SHELL_ATTR, shellMode);
    setAttr(root, MOTION_ATTR, !STARLIGHT_ENABLED ? "off" : document.hidden ? "paused" : "on");
    setStarlightPalette(shellMode);

    for (const [key, value] of Object.entries(THEME.colors || {})) setVar(`--cts-color-${key}`, value);
    for (const [key, value] of Object.entries(THEME.strings || {})) setVar(`--cts-str-${key}`, JSON.stringify(String(value)));

    let style = document.getElementById(STYLE_ID);
    if (!style) {
      style = document.createElement("style");
      style.id = STYLE_ID;
      (document.head || root).appendChild(style);
    }
    if (style.dataset.ctsStamp !== STAMP) {
      style.textContent = `${cssText}\n\n${RUNTIME_CSS}`;
      style.dataset.ctsStamp = STAMP;
    }

    const shellMain = document.querySelector("main.main-surface") || document.querySelector("main");
    integrateWindowsMenu(shellMain);
    const shellAttached = integrateShellSeam(shellMain);
    const home = findHome(state?.homeSticky);
    if (state) state.homeSticky = home;
    for (const candidate of document.querySelectorAll('[role="main"].cts-home')) {
      if (candidate !== home) candidate.classList.remove("cts-home");
    }
    if (home) setClass(home, "cts-home", true);
    if (shellMain) setClass(shellMain, "cts-home-shell", Boolean(home));

    annotateIcons();
    annotateComposerOverflow(selectComposerSurfaces(document));

    const fillTexts = (rootNode) => {
      for (const node of rootNode.querySelectorAll("[data-cts-text]")) {
        const key = node.getAttribute("data-cts-text");
        const value = (THEME.strings || {})[key];
        if (typeof value === "string" && node.textContent !== value) node.textContent = value;
      }
    };

    // Stage layer: theme scenery INSIDE main, painted UNDER the app content
    // (main > * are lifted to z-index 1 by the theme CSS). Never overlays
    // dialogs, popovers or panels.
    if (layers.stageHtml && shellMain) {
      let stage = document.getElementById(STAGE_ID);
      if (!stage || stage.parentElement !== shellMain) {
        stage?.remove();
        stage = document.createElement("div");
        stage.id = STAGE_ID;
        stage.setAttribute("aria-hidden", "true");
        stage.style.position = "absolute";
        stage.style.inset = "0";
        stage.style.zIndex = "0";
        stage.style.pointerEvents = "none";
        stage.style.overflow = "hidden";
        shellMain.prepend(stage);
      }
      if (stage.dataset.ctsStamp !== STAMP) {
        stage.innerHTML = layers.stageHtml;
        stage.dataset.ctsStamp = STAMP;
      }
      fillTexts(stage);
      setClass(stage, "cts-home-shell", Boolean(home));
      ensureStarlight(stage);
    } else if (!layers.stageHtml) {
      document.getElementById(STARLIGHT_ID)?.remove();
      document.getElementById(STAGE_ID)?.remove();
    }

    // Decorative chrome overlay — strictly non-interactive. Full-screen
    // routes (Settings) unmount the shell: hide the chrome entirely there.
    const existingChrome = document.getElementById(CHROME_ID);
    if (existingChrome) {
      const wantVisible = Boolean(layers.overlayHtml && shellMain);
      const visibleNow = existingChrome.style.display !== "none";
      if (visibleNow !== wantVisible) existingChrome.style.display = wantVisible ? "" : "none";
    }
    if (layers.overlayHtml && shellMain) {
      let chrome = document.getElementById(CHROME_ID);
      if (!chrome || chrome.parentElement !== document.body) {
        chrome?.remove();
        chrome = document.createElement("div");
        chrome.id = CHROME_ID;
        chrome.setAttribute("aria-hidden", "true");
        chrome.style.position = "fixed";
        chrome.style.pointerEvents = "none";
        chrome.style.overflow = "hidden";
        chrome.style.zIndex = "31";
        document.body.appendChild(chrome);
      }
      if (chrome.dataset.ctsStamp !== STAMP) {
        chrome.innerHTML = layers.overlayHtml;
        chrome.dataset.ctsStamp = STAMP;
      }
      fillTexts(chrome);
      const box = shellMain.getBoundingClientRect();
      const next = {
        left: Math.round(box.left), top: Math.round(box.top),
        width: Math.round(box.width), height: Math.round(box.height),
      };
      if (next.left !== chromeRectCache.left || next.top !== chromeRectCache.top ||
          next.width !== chromeRectCache.width || next.height !== chromeRectCache.height) {
        Object.assign(chromeRectCache, next);
        chrome.style.left = `${next.left}px`;
        chrome.style.top = `${next.top}px`;
        chrome.style.width = `${next.width}px`;
        chrome.style.height = `${next.height}px`;
      }
      setClass(chrome, "cts-home-shell", Boolean(home));
      setClass(chrome, ATTACHED_SIDEBAR_CLASS, shellAttached);
      setAttr(chrome, SHELL_ATTR, root.getAttribute(SHELL_ATTR) || "light");
    } else if (!layers.overlayHtml) {
      document.getElementById(CHROME_ID)?.remove();
    }
  };

  const cleanup = () => {
    window[DISABLED_KEY] = true;
    const root = document.documentElement;
    root?.classList.remove(ROOT_CLASS);
    root?.removeAttribute(THEME_ATTR);
    root?.removeAttribute(SHELL_ATTR);
    root?.removeAttribute(MOTION_ATTR);
    const state = window[STATE_KEY];
    for (const name of state?.appliedVars ?? appliedVars) root?.style.removeProperty(name);
    document.querySelectorAll(".cts-home").forEach((node) => node.classList.remove("cts-home"));
    document.querySelectorAll(".cts-home-shell").forEach((node) => node.classList.remove("cts-home-shell"));
    document.querySelectorAll("[data-cts-glyph]").forEach((node) => node.removeAttribute("data-cts-glyph"));
    document.querySelectorAll("[data-cts-icon]").forEach((node) => node.removeAttribute("data-cts-icon"));
    document.querySelectorAll("[data-cts-logo]").forEach((node) => node.removeAttribute("data-cts-logo"));
    document.querySelectorAll(`.${WINDOWS_MENU_CLASS}`).forEach((node) => node.classList.remove(WINDOWS_MENU_CLASS));
    document.querySelectorAll(`[${WINDOWS_MENU_REGION_ATTR}]`).forEach((node) => node.removeAttribute(WINDOWS_MENU_REGION_ATTR));
    document.querySelectorAll(`[${WINDOWS_MENU_COMMAND_GROUP_ATTR}]`).forEach((node) => node.removeAttribute(WINDOWS_MENU_COMMAND_GROUP_ATTR));
    document.querySelectorAll(`.${ATTACHED_MAIN_CLASS}, .${ATTACHED_SIDEBAR_CLASS}`)
      .forEach((node) => node.classList.remove(ATTACHED_MAIN_CLASS, ATTACHED_SIDEBAR_CLASS));
    document.querySelectorAll(`[${COMPOSER_OVERFLOW_ATTR}]`)
      .forEach((node) => node.removeAttribute(COMPOSER_OVERFLOW_ATTR));
    document.querySelectorAll(`[${COMPOSER_MODE_ATTR}]`)
      .forEach((node) => node.removeAttribute(COMPOSER_MODE_ATTR));
    document.getElementById(STYLE_ID)?.remove();
    document.getElementById(CHROME_ID)?.remove();
    document.getElementById(STAGE_ID)?.remove();
    document.getElementById(INTRO_ID)?.remove();
    document.getElementById(STARLIGHT_ID)?.remove();
    state?.observer?.disconnect();
    if (state?.timer) clearInterval(state.timer);
    if (state?.clock) clearInterval(state.clock);
    if (state?.scheduler?.timeout) clearTimeout(state.scheduler.timeout);
    if (state?.resizeHandler) window.removeEventListener("resize", state.resizeHandler);
    if (state?.visibilityHandler) document.removeEventListener("visibilitychange", state.visibilityHandler);
    if (state?.mediaHandler && state?.mediaQuery) {
      try { state.mediaQuery.removeEventListener("change", state.mediaHandler); } catch {}
    }
    delete window[STATE_KEY];
    return true;
  };

  const scheduler = { timeout: null };
  const scheduleEnsure = () => {
    if (scheduler.timeout) clearTimeout(scheduler.timeout);
    scheduler.timeout = setTimeout(() => {
      scheduler.timeout = null;
      ensure();
    }, 180);
  };

  // Ignore mutations we caused ourselves (chrome text/position, clock ticks,
  // root inline vars) — they must never re-trigger ensure().
  const chromeNode = () => document.getElementById(CHROME_ID);
  const observer = new MutationObserver((mutations) => {
    const chrome = chromeNode();
    for (const mutation of mutations) {
      const target = mutation.target;
      if (chrome && (target === chrome || chrome.contains(target))) continue;
      if (target === document.documentElement && mutation.type === "attributes" && mutation.attributeName === "style") continue;
      scheduleEnsure();
      return;
    }
  });
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["class", "data-theme", "data-appearance", "data-color-mode"],
  });
  const timer = setInterval(ensure, 4000);
  const resizeHandler = scheduleEnsure;
  window.addEventListener("resize", resizeHandler, { passive: true });
  const visibilityHandler = scheduleEnsure;
  document.addEventListener("visibilitychange", visibilityHandler, { passive: true });

  // Live tactical clock — writes only textContent inside #cts-chrome, which
  // the observer filter above ignores.
  const clock = setInterval(() => {
    const node = document.querySelector(`#${CHROME_ID} [data-cts-clock]`);
    if (!node) return;
    const now = new Date();
    const two = (n) => String(n).padStart(2, "0");
    const text = `${two(now.getHours())}:${two(now.getMinutes())}:${two(now.getSeconds())}`;
    if (node.textContent !== text) node.textContent = text;
  }, 1000);

  let mediaQuery = null;
  let mediaHandler = null;
  try {
    mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    mediaHandler = () => scheduleEnsure();
    mediaQuery.addEventListener("change", mediaHandler);
  } catch {}

  window[STATE_KEY] = {
    ensure, cleanup, observer, timer, clock, scheduler, resizeHandler, visibilityHandler,
    mediaQuery, mediaHandler, appliedVars,
    homeSticky: null,
    stamp: STAMP,
    version: VERSION,
    themeId: THEME.id || "custom",
    starlightEnabled: STARLIGHT_ENABLED,
  };
  ensure();

  // Rise! — transformation intro, played once per fresh theme load (not on
  // idempotent re-ensures). Skips quietly when the punch art is absent or
  // the user prefers reduced motion.
  const playIntro = () => {
    try {
      if (!STARLIGHT_ENABLED) return;
      if (document.hidden) return;
      if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
      if (document.getElementById(INTRO_ID) || !document.body) return;
      // Theme-agnostic convention: themes register their intro art as the
      // asset key "intro"; --cts-asset-tiga-punch is the legacy fallback. An
      // optional "intro-video" motion asset takes priority while the static
      // art remains its poster/fallback when playback is unavailable.
      const styles = getComputedStyle(document.documentElement);
      const art = styles.getPropertyValue("--cts-asset-intro") || styles.getPropertyValue("--cts-asset-tiga-punch");
      const videoSrc = typeof MOTION["intro-video"] === "string" ? MOTION["intro-video"] : "";
      if ((!art || !art.trim()) && !videoSrc) return;
      const durationValue = styles.getPropertyValue("--cts-intro-duration").trim();
      const durationMatch = durationValue.match(/^(\d+(?:\.\d+)?)(ms|s)$/i);
      const durationMs = durationMatch
        ? Math.min(15000, Math.max(1000, Number(durationMatch[1]) * (durationMatch[2].toLowerCase() === "s" ? 1000 : 1)))
        : 2500;
      const mountIntro = (videoError) => {
        document.getElementById(INTRO_ID)?.remove();
        const intro = document.createElement("div");
        intro.id = INTRO_ID;
        intro.setAttribute("aria-hidden", "true");
        if (videoError) intro.dataset.ctsVideoError = videoError;
        intro.innerHTML = '<i class="cts-intro-rays"></i><b class="cts-intro-figure"></b><u class="cts-intro-flash"></u>';
        document.body.appendChild(intro);
        setTimeout(() => intro.remove(), durationMs + 120);
        return intro;
      };
      const intro = mountIntro();
      // A video that fails mid-play must not strand the fallback inside a
      // parent whose animation timeline already ran out: remount the intro
      // from scratch so the static art restarts cleanly (or clear it when the
      // theme ships no static intro at all). The callbacks are async — after
      // a hot switch or `off`, the removed video rejects play() with
      // AbortError and this closure fires against a world it no longer owns,
      // so it must verify the intro is still ours (and only fall back once:
      // the error event and the play rejection often arrive together).
      let fellBack = false;
      const fallbackToStatic = (reason) => {
        if (fellBack || window[DISABLED_KEY]) return;
        if (document.getElementById(INTRO_ID) !== intro) return;
        fellBack = true;
        if (art && art.trim()) mountIntro(reason);
        else intro.remove();
      };
      if (videoSrc) {
        const video = document.createElement("video");
        video.className = "cts-intro-video";
        video.src = videoSrc;
        video.autoplay = true;
        video.muted = true;
        video.defaultMuted = true;
        video.playsInline = true;
        video.preload = "auto";
        video.controls = false;
        video.disablePictureInPicture = true;
        video.setAttribute("muted", "");
        video.setAttribute("playsinline", "");
        video.addEventListener("error", () => {
          const mediaError = video.error;
          fallbackToStatic(mediaError
            ? `${mediaError.code}:${mediaError.message || "media error"}`
            : "media error");
        }, { once: true });
        intro.prepend(video);
        try {
          const playing = video.play();
          playing?.catch?.((error) => fallbackToStatic(`${error?.name || "play"}:${error?.message || "rejected"}`));
        } catch (error) {
          fallbackToStatic(`${error?.name || "play"}:${error?.message || "failed"}`);
        }
      }
    } catch { /* cosmetic only */ }
  };
  if (previous?.stamp !== STAMP) playIntro();

  return { installed: true, version: VERSION, themeId: THEME.id || "custom", starlightEnabled: STARLIGHT_ENABLED };
})(__CTS_CSS_JSON__, __CTS_THEME_JSON__, __CTS_CHROME_JSON__, __CTS_MOTION_JSON__, __CTS_STARLIGHT_JSON__)
