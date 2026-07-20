((cssText, artDataUrl, skinMeta) => {
  const STATE_KEY = "__CODEX_DREAM_SKIN_STATE__";
  const STYLE_ID = "codex-dream-skin-style";
  const CHROME_ID = "codex-dream-skin-chrome";
  const WINDOWS_MENU_CLASS = "dream-windows-menu-bar";
  const WINDOWS_MENU_REGION_ATTR = "data-dream-menu-region";
  const ATTACHED_MAIN_CLASS = "dream-shell-attached-main";
  const ATTACHED_SIDEBAR_CLASS = "dream-shell-attached-sidebar";
  window.__CODEX_DREAM_SKIN_DISABLED__ = false;

  const previous = window[STATE_KEY];
  if (previous?.observer) previous.observer.disconnect();
  if (previous?.timer) clearInterval(previous.timer);
  if (previous?.scheduler?.timeout) clearTimeout(previous.scheduler.timeout);
  if (previous?.artUrl) URL.revokeObjectURL(previous.artUrl);
  const artUrl = (() => {
    const comma = artDataUrl.indexOf(",");
    const binary = atob(artDataUrl.slice(comma + 1));
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    return URL.createObjectURL(new Blob([bytes], { type: "image/png" }));
  })();
  const meta = {
    id: typeof skinMeta?.id === "string" ? skinMeta.id : "coral-haze",
    brandName: typeof skinMeta?.brandName === "string" ? skinMeta.brandName : "晨雾珊瑚 · 专属皮肤",
    brandSubtitle: typeof skinMeta?.brandSubtitle === "string" ? skinMeta.brandSubtitle : "Codex App 柔光限定版 ✦",
    signature: typeof skinMeta?.signature === "string" ? skinMeta.signature : "Coral Haze ♡",
    starlightEnabled: skinMeta?.starlightEnabled !== false,
  };
  const effectCssText = `
html.codex-dream-skin.codex-dream-starlight-off .dream-sparkles,
html.codex-dream-skin.codex-dream-starlight-off #codex-dream-skin-chrome::before,
html.codex-dream-skin.codex-dream-starlight-off #codex-dream-skin-chrome::after,
html.codex-dream-skin.codex-dream-starlight-off main.main-surface::after,
html.codex-dream-skin.codex-dream-starlight-off .dream-home > div:first-child > div:first-child > div:first-child::after {
  opacity: 0 !important;
  animation: none !important;
}
html.codex-dream-skin.codex-dream-starlight-off .dream-sparkles,
html.codex-dream-skin.codex-dream-starlight-off .dream-sparkles *,
html.codex-dream-skin.codex-dream-starlight-off .dream-sparkles *::before,
html.codex-dream-skin.codex-dream-starlight-off .dream-sparkles *::after {
  animation: none !important;
  transition: none !important;
}
html.codex-dream-skin .dream-windows-menu-bar {
  position: absolute !important;
  inset: 0 0 auto 0 !important;
  height: var(--dream-windows-menu-height, 36px) !important;
}
html.codex-dream-skin .dream-windows-menu-bar + * > aside.app-shell-left-panel {
  padding-top: calc(var(--dream-windows-menu-height, 36px) + var(--dream-windows-sidebar-padding-top, 0px)) !important;
}
html.codex-dream-skin .dream-windows-menu-bar + * > main.main-surface {
  padding-top: calc(var(--dream-windows-menu-height, 36px) + var(--dream-windows-main-padding-top, 0px)) !important;
}
html.codex-dream-skin .dream-windows-menu-bar [data-dream-menu-region="sidebar"] {
  color: var(--dream-windows-sidebar-foreground) !important;
  -webkit-text-fill-color: var(--dream-windows-sidebar-foreground) !important;
}
html.codex-dream-skin .dream-windows-menu-bar [data-dream-menu-region="main"] {
  color: var(--dream-windows-main-foreground) !important;
  -webkit-text-fill-color: var(--dream-windows-main-foreground) !important;
}
html.codex-dream-skin aside.app-shell-left-panel.dream-shell-attached-main {
  border-top-right-radius: 0 !important;
  border-bottom-right-radius: 0 !important;
}
html.codex-dream-skin main.main-surface.dream-shell-attached-sidebar,
html.codex-dream-skin main.main-surface.dream-shell-attached-sidebar > header.app-header-tint,
html.codex-dream-skin #codex-dream-skin-chrome.dream-shell-attached-sidebar {
  border-top-left-radius: 0 !important;
  border-bottom-left-radius: 0 !important;
}
`;
  const runtimeCssText = `${cssText}\n${effectCssText}`;
  const existingStyle = document.getElementById(STYLE_ID);
  if (existingStyle) {
    existingStyle.textContent = runtimeCssText;
    existingStyle.dataset.dreamVersion = "3";
  }

  const setClass = (node, name, enabled) => {
    if (node && node.classList.contains(name) !== enabled) node.classList.toggle(name, enabled);
  };

  const setRootVar = (name, value) => {
    const root = document.documentElement;
    if (root && root.style.getPropertyValue(name) !== value) root.style.setProperty(name, value);
  };

  const integrateWindowsShell = (shellMain) => {
    const root = document.documentElement;
    const menu = document.querySelector('.app-header-tint[class~="group/application-menu-top-bar"]');
    const shellRow = menu?.nextElementSibling;
    const sidebar = shellRow?.querySelector(":scope > aside.app-shell-left-panel");
    const main = shellRow?.querySelector(":scope > main.main-surface");
    const menuBox = menu?.getBoundingClientRect();
    const integrated = Boolean(menu?.classList.contains(WINDOWS_MENU_CLASS));
    const eligible = Boolean(menu && sidebar && main && main === shellMain &&
      menuBox && menuBox.width > 0 && menuBox.height > 0);

    document.querySelectorAll(`.${WINDOWS_MENU_CLASS}`).forEach((node) => {
      if (!eligible || node !== menu) node.classList.remove(WINDOWS_MENU_CLASS);
    });
    document.querySelectorAll(`[${WINDOWS_MENU_REGION_ATTR}]`).forEach((node) => {
      if (!eligible || !menu.contains(node)) node.removeAttribute(WINDOWS_MENU_REGION_ATTR);
    });
    document.querySelectorAll(`.${ATTACHED_MAIN_CLASS}`).forEach((node) => {
      if (!eligible || node !== sidebar) node.classList.remove(ATTACHED_MAIN_CLASS);
    });
    document.querySelectorAll(`main.${ATTACHED_SIDEBAR_CLASS}`).forEach((node) => {
      if (!eligible || node !== main) node.classList.remove(ATTACHED_SIDEBAR_CLASS);
    });

    if (!eligible) {
      for (const name of [
        "--dream-windows-menu-height", "--dream-windows-sidebar-padding-top",
        "--dream-windows-main-padding-top", "--dream-windows-sidebar-foreground",
        "--dream-windows-main-foreground",
      ]) root?.style.removeProperty(name);
      return false;
    }

    const sidebarStyle = getComputedStyle(sidebar);
    const mainStyle = getComputedStyle(main);
    const appliedOffset = integrated ? menuBox.height : 0;
    const basePadding = (style) =>
      `${Math.max(0, (Number.parseFloat(style.paddingTop) || 0) - appliedOffset)}px`;
    setRootVar("--dream-windows-menu-height", `${menuBox.height}px`);
    setRootVar("--dream-windows-sidebar-padding-top", basePadding(sidebarStyle));
    setRootVar("--dream-windows-main-padding-top", basePadding(mainStyle));
    setClass(menu, WINDOWS_MENU_CLASS, true);
    setRootVar("--dream-windows-sidebar-foreground", sidebarStyle.color);
    setRootVar("--dream-windows-main-foreground", mainStyle.color);

    const sidebarBox = sidebar.getBoundingClientRect();
    const mainBox = main.getBoundingClientRect();
    const attached = sidebarBox.width > 1 && mainBox.width > 1 &&
      Math.abs(sidebarBox.right - mainBox.left) <= 2 &&
      Math.abs(sidebarBox.top - mainBox.top) <= 2;
    setClass(sidebar, ATTACHED_MAIN_CLASS, attached);
    setClass(main, ATTACHED_SIDEBAR_CLASS, attached);

    for (const control of menu.querySelectorAll("button, [role=button]")) {
      const box = control.getBoundingClientRect();
      const region = box.left + box.width / 2 <= sidebarBox.right ? "sidebar" : "main";
      if (control.getAttribute(WINDOWS_MENU_REGION_ATTR) !== region)
        control.setAttribute(WINDOWS_MENU_REGION_ATTR, region);
    }
    return attached;
  };

  const ensure = () => {
    if (window.__CODEX_DREAM_SKIN_DISABLED__) return;
    const root = document.documentElement;
    if (!root) return;
    root.classList.add("codex-dream-skin");
    root.classList.toggle("codex-dream-starlight-off", !meta.starlightEnabled);
    root.dataset.dreamSkinId = meta.id;
    root.style.setProperty("--dream-art", `url("${artUrl}")`);

    let style = document.getElementById(STYLE_ID);
    if (!style) {
      style = document.createElement("style");
      style.id = STYLE_ID;
      (document.head || root).appendChild(style);
    }
    if (style.dataset.dreamVersion !== "3") {
      style.textContent = runtimeCssText;
      style.dataset.dreamVersion = "3";
    }

    const shellMain = document.querySelector("main.main-surface") || document.querySelector("main");
    const shellAttached = integrateWindowsShell(shellMain);
    const home = document.querySelector('[role="main"]:has([data-testid="home-icon"])');
    for (const candidate of document.querySelectorAll('[role="main"].dream-home')) {
      if (candidate !== home) candidate.classList.remove("dream-home");
    }
    if (home) home.classList.add("dream-home");

    const taskButtons = home
      ? [...home.querySelectorAll('button[class~="group/home-suggestion-list-item"]')]
      : [];
    const taskContainer = taskButtons.length > 1
      ? taskButtons[0].parentElement?.parentElement
      : null;
    const hasSharedTaskContainer = Boolean(taskContainer) && taskButtons.every(
      (button) => button.parentElement?.parentElement === taskContainer,
    );
    document.querySelectorAll(".dream-task-suggestion").forEach((node) => {
      if (!taskButtons.includes(node)) node.classList.remove("dream-task-suggestion");
    });
    document.querySelectorAll(".dream-task-suggestions").forEach((node) => {
      if (node !== taskContainer || !hasSharedTaskContainer) node.classList.remove("dream-task-suggestions");
    });
    taskButtons.forEach((button) => button.classList.toggle("dream-task-suggestion", hasSharedTaskContainer));
    if (hasSharedTaskContainer) taskContainer.classList.add("dream-task-suggestions");
    home?.classList.toggle("dream-home-task-mode", hasSharedTaskContainer);

    if (!shellMain || !document.body) return;
    shellMain.classList.toggle("dream-home-shell", Boolean(home));
    let chrome = document.getElementById(CHROME_ID);
    if (!chrome || chrome.parentElement !== document.body) {
      chrome?.remove();
      chrome = document.createElement("div");
      chrome.id = CHROME_ID;
      chrome.setAttribute("aria-hidden", "true");
      chrome.innerHTML = `
        <div class="dream-brand"><span class="dream-note">✦</span><span><b></b><small></small></span></div>
        <div class="dream-signature"></div>
        <div class="dream-sparkles"><i></i><i></i><i></i><i></i><i></i><i></i></div>
        <div class="dream-ribbon"><span>♡</span>✦<span>♡</span></div>
        <div class="dream-polaroid"></div>`;
      document.body.appendChild(chrome);
    }
    chrome.querySelector(".dream-brand b").textContent = meta.brandName;
    chrome.querySelector(".dream-brand small").textContent = meta.brandSubtitle;
    chrome.querySelector(".dream-signature").textContent = meta.signature;
    const shellBox = shellMain.getBoundingClientRect();
    chrome.style.left = `${Math.round(shellBox.left)}px`;
    chrome.style.top = `${Math.round(shellBox.top)}px`;
    chrome.style.width = `${Math.round(shellBox.width)}px`;
    chrome.style.height = `${Math.round(shellBox.height)}px`;
    chrome.classList.toggle("dream-home-shell", Boolean(home));
    chrome.classList.toggle(ATTACHED_SIDEBAR_CLASS, shellAttached);
  };

  const cleanup = () => {
    window.__CODEX_DREAM_SKIN_DISABLED__ = true;
    document.documentElement?.classList.remove("codex-dream-skin");
    document.documentElement?.classList.remove("codex-dream-starlight-off");
    if (document.documentElement) delete document.documentElement.dataset.dreamSkinId;
    document.documentElement?.style.removeProperty("--dream-art");
    document.querySelectorAll(".dream-home").forEach((node) => node.classList.remove("dream-home"));
    document.querySelectorAll(".dream-home-shell").forEach((node) => node.classList.remove("dream-home-shell"));
    document.querySelectorAll(".dream-home-task-mode").forEach((node) => node.classList.remove("dream-home-task-mode"));
    document.querySelectorAll(".dream-task-suggestions").forEach((node) => node.classList.remove("dream-task-suggestions"));
    document.querySelectorAll(".dream-task-suggestion").forEach((node) => node.classList.remove("dream-task-suggestion"));
    document.querySelectorAll(`.${WINDOWS_MENU_CLASS}`).forEach((node) => node.classList.remove(WINDOWS_MENU_CLASS));
    document.querySelectorAll(`[${WINDOWS_MENU_REGION_ATTR}]`).forEach((node) => node.removeAttribute(WINDOWS_MENU_REGION_ATTR));
    document.querySelectorAll(`.${ATTACHED_MAIN_CLASS}`).forEach((node) => node.classList.remove(ATTACHED_MAIN_CLASS));
    document.querySelectorAll(`.${ATTACHED_SIDEBAR_CLASS}`).forEach((node) => node.classList.remove(ATTACHED_SIDEBAR_CLASS));
    for (const name of [
      "--dream-windows-menu-height", "--dream-windows-sidebar-padding-top",
      "--dream-windows-main-padding-top", "--dream-windows-sidebar-foreground",
      "--dream-windows-main-foreground",
    ]) document.documentElement?.style.removeProperty(name);
    document.getElementById(STYLE_ID)?.remove();
    document.getElementById(CHROME_ID)?.remove();
    const state = window[STATE_KEY];
    state?.observer?.disconnect();
    if (state?.timer) clearInterval(state.timer);
    if (state?.scheduler?.timeout) clearTimeout(state.scheduler.timeout);
    if (state?.artUrl) URL.revokeObjectURL(state.artUrl);
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
  const observer = new MutationObserver(scheduleEnsure);
  observer.observe(document.documentElement, { childList: true, subtree: true });
  const timer = setInterval(ensure, 5000);
  window[STATE_KEY] = { ensure, cleanup, observer, timer, scheduler, artUrl, skinId: meta.id,
    starlightEnabled: meta.starlightEnabled, version: "2.0.0" };
  ensure();
  return { installed: true, skinId: meta.id, starlightEnabled: meta.starlightEnabled, version: "2.0.0" };
})(__DREAM_CSS_JSON__, __DREAM_ART_JSON__, __DREAM_META_JSON__)
