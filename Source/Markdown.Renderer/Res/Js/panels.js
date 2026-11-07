(() => {
    const root = document.documentElement;
    const themeToggle = document.getElementById("theme-toggle");
    const themes = ["auto", "light", "dark"];
    const themeNames = { auto: "Auto", light: "Light", dark: "Dark" };

    const updateThemeLabel = () => {
        const currentTheme = root.dataset.theme;
        const nextTheme = themes[(themes.indexOf(currentTheme) + 1) % themes.length];
        themeToggle.title = `Theme: ${themeNames[currentTheme]} (click for ${themeNames[nextTheme]})`;
        themeToggle.setAttribute("aria-label", `Theme: ${themeNames[currentTheme]}. Click to switch to ${themeNames[nextTheme]}.`);
    };

    new MutationObserver(updateThemeLabel)
        .observe(root, { attributes: true, attributeFilter: ["data-theme"] });

    updateThemeLabel();
    themeToggle.addEventListener("click", () => {
        const currentTheme = root.dataset.theme;
        const nextTheme = themes[(themes.indexOf(currentTheme) + 1) % themes.length];
        root.dataset.theme = nextTheme;
        try {
            localStorage.setItem("mdu-theme", nextTheme);
        } catch { }
        updateThemeLabel();
    });

    const layout = document.getElementById("site-layout");
    const header = document.getElementById("site-header");
    const toggle = document.getElementById("navigation-toggle");
    const backdrop = document.getElementById("sidebar-backdrop");
    const navigation = document.getElementById("site-navigation");
    const sidebarResizer = document.getElementById("sidebar-resizer");
    const panels = mduPanels;
    const breakpoint = panels.mobile;
    const compactNavigation = panels.compact;
    const main = layout.querySelector(".site-main");
    const toc = main.querySelector(".on-this-page");
    const hasTableOfContents = toc !== null;
    const tocToggle = document.getElementById("toc-toggle");
    const tocResizer = document.getElementById("toc-resizer");
    const tocIsCollapsed = () => root.classList.contains("toc-collapsed");
    const sidebarPanel = { ...panels.sidebar, resizer: sidebarResizer, element: navigation };
    const tocPanel = { ...panels.toc, resizer: tocResizer, element: toc };
    for (const panel of [sidebarPanel, tocPanel]) {
        panel.preferred = parseFloat(root.style.getPropertyValue(panel.property));
    }
    let panelFrame = null;
    const observedWidths = new WeakMap();
    const syncPanels = () => {
        if (panelFrame !== null) cancelAnimationFrame(panelFrame);
        panelFrame = null;
        const layoutStyle = getComputedStyle(layout);
        const mainStyle = getComputedStyle(main);
        const availableWidth = layout.clientWidth - parseFloat(layoutStyle.paddingLeft) - parseFloat(layoutStyle.paddingRight);
        const mainPadding = parseFloat(mainStyle.paddingLeft) + parseFloat(mainStyle.paddingRight);
        const sidebarVisible = !breakpoint.matches && !layout.classList.contains("sidebar-collapsed") &&
            (compactNavigation.matches || layout.classList.contains("has-wide-sidebar"));
        const measured = panels.measure(availableWidth, mainPadding, sidebarVisible, hasTableOfContents);
        root.classList.toggle("toc-space-available", measured.tocFits);
        const mainWidth = main.clientWidth;
        observedWidths.set(layout, Math.round(layout.clientWidth));
        observedWidths.set(main, Math.round(mainWidth));
        const contentWidth = mainWidth - mainPadding;
        tocToggle.hidden = !measured.tocFits;
        tocResizer.hidden = !measured.tocFits || tocIsCollapsed();
        tocToggle.setAttribute("aria-expanded", String(!tocIsCollapsed()));
        const label = `${tocIsCollapsed() ? "Expand" : "Collapse"} table of contents`;
        tocToggle.setAttribute("aria-label", label);
        tocToggle.title = label;
        sidebarPanel.availableMax = measured.sidebarMax;
        tocPanel.availableMax = Math.round(Math.max(panels.toc.min, Math.min(panels.toc.max,
            contentWidth - panels.tocContentMin - panels.gap)));
        for (const panel of [sidebarPanel, tocPanel]) {
            const width = Math.round(panel.element?.getBoundingClientRect().width ?? 0);
            if (panel.element) observedWidths.set(panel.element, width);
            panel.resizer.setAttribute("aria-valuemin", String(panel.min));
            panel.resizer.setAttribute("aria-valuemax", String(panel.availableMax));
            panel.resizer.setAttribute("aria-valuenow", String(width));
        }
    };
    const schedulePanelSync = () => {
        if (panelFrame === null) panelFrame = requestAnimationFrame(syncPanels);
    };
    const setPanelWidth = (panel, width, save = true) => {
        const clampedWidth = Math.round(Math.max(panel.min, Math.min(panel.availableMax, width)));
        root.style.setProperty(panel.property, `${clampedWidth}px`);
        if (save) {
            panel.preferred = clampedWidth;
            try {
                localStorage.setItem(panel.key, String(clampedWidth));
            } catch { }
        }
        syncPanels();
    };
    const restorePanelWidth = (panel) => {
        root.style.setProperty(panel.property, `${panel.preferred}px`);
        syncPanels();
    };

    const bindPanelResizer = (panel, direction) => {
        const resizer = panel.resizer;
        let activePointerId = null;
        let pointerStartX = 0;
        let pointerStartWidth = 0;
        let moved = false;
        let pendingWidth = null;
        let dragFrame = null;
        const applyDrag = () => {
            if (dragFrame !== null) cancelAnimationFrame(dragFrame);
            dragFrame = null;
            if (pendingWidth === null) return;
            const width = pendingWidth;
            pendingWidth = null;
            setPanelWidth(panel, width, false);
        };
        const discardDrag = () => {
            if (dragFrame !== null) cancelAnimationFrame(dragFrame);
            dragFrame = null;
            pendingWidth = null;
        };
        resizer.addEventListener("pointerdown", (event) => {
            if (event.button !== 0 || activePointerId !== null) return;
            syncPanels();
            pointerStartX = event.clientX;
            pointerStartWidth = Number(resizer.getAttribute("aria-valuenow"));
            activePointerId = event.pointerId;
            moved = false;
            resizer.setPointerCapture(event.pointerId);
            event.preventDefault();
        });
        resizer.addEventListener("pointermove", (event) => {
            if (event.pointerId !== activePointerId) return;
            if (event.clientX === pointerStartX && !moved) return;
            moved = true;
            pendingWidth = pointerStartWidth + direction * (event.clientX - pointerStartX);
            if (dragFrame === null) dragFrame = requestAnimationFrame(applyDrag);
        });
        const finishResize = (event) => {
            if (event.pointerId !== activePointerId) return;
            activePointerId = null;
            if (moved) {
                if (event.type === "pointerup") {
                    pendingWidth = pointerStartWidth + direction * (event.clientX - pointerStartX);
                    applyDrag();
                } else discardDrag();
                const width = Number(resizer.getAttribute("aria-valuenow"));
                if (event.type === "pointerup" && width !== pointerStartWidth) setPanelWidth(panel, width);
                else restorePanelWidth(panel);
            }
        };
        resizer.addEventListener("pointerup", finishResize);
        resizer.addEventListener("pointercancel", finishResize);
        resizer.addEventListener("lostpointercapture", (event) => {
            if (event.pointerId === activePointerId) {
                activePointerId = null;
                discardDrag();
                if (moved) restorePanelWidth(panel);
            }
        });
        resizer.addEventListener("keydown", (event) => {
            if (event.altKey || event.metaKey || event.ctrlKey) return;
            if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            syncPanels();
            const currentWidth = Number(resizer.getAttribute("aria-valuenow"));
            const step = event.shiftKey ? 50 : 10;
            let nextWidth;
            if (event.key === "ArrowLeft") nextWidth = currentWidth - direction * step;
            else if (event.key === "ArrowRight") nextWidth = currentWidth + direction * step;
            else if (event.key === "Home") nextWidth = Number(resizer.getAttribute("aria-valuemin"));
            else if (event.key === "End") nextWidth = Number(resizer.getAttribute("aria-valuemax"));
            else return;
            event.preventDefault();
            const clampedWidth = Math.round(Math.max(panel.min, Math.min(panel.availableMax, nextWidth)));
            if (clampedWidth !== currentWidth) setPanelWidth(panel, clampedWidth);
        });
    };
    bindPanelResizer(sidebarPanel, 1);
    bindPanelResizer(tocPanel, -1);
    tocToggle.addEventListener("click", () => {
        root.classList.toggle("toc-collapsed");
        try {
            localStorage.setItem("mdu-toc-collapsed", String(tocIsCollapsed()));
        } catch { }
        syncPanels();
    });
    const panelObserver = new ResizeObserver((entries) => {
        if (entries.some(entry => observedWidths.get(entry.target) !== Math.round(entry.borderBoxSize[0].inlineSize))) {
            schedulePanelSync();
        }
    });
    panelObserver.observe(layout);
    panelObserver.observe(main);
    panelObserver.observe(navigation);
    if (hasTableOfContents) panelObserver.observe(toc);

    const setDesktopSidebarCollapsed = (collapsed) => {
        try {
            sessionStorage.setItem(panels.sidebarPreferenceKey, String(collapsed));
        } catch { }
    };
    // A dashboard uses the whole width; its navigation stays one click away.
    const isDashboard = layout.classList.contains("is-dashboard");
    const startsCollapsed = panels.startsCollapsed;
    let isOpen = !breakpoint.matches && !startsCollapsed();

    const setOpen = (open) => {
        isOpen = open;
        layout.classList.toggle("sidebar-open", open && breakpoint.matches);
        layout.classList.toggle("sidebar-collapsed", !open && !breakpoint.matches);
        root.classList.toggle("sidebar-starts-collapsed", !open && !breakpoint.matches);
        panels.updateNavigationHeader(header, toggle, open);
        syncPanels();

        if (open && breakpoint.matches) {
            navigation.querySelector("a")?.focus();
        } else if (!open && navigation.contains(document.activeElement)) {
            toggle.focus();
        }
    };

    setOpen(isOpen);
    toggle.addEventListener("click", () => {
        const open = !isOpen;
        setOpen(open);
        if (!breakpoint.matches) setDesktopSidebarCollapsed(!open);
    });

    backdrop.addEventListener("click", () => setOpen(false));
    navigation.addEventListener("click", (event) => {
        if (breakpoint.matches && event.target.closest("a")) setOpen(false);
    });
    document.addEventListener("keydown", (event) => {
        if (breakpoint.matches && isOpen && event.key === "Escape") setOpen(false);
    });
    breakpoint.addEventListener("change", (event) =>
        setOpen(!event.matches && !startsCollapsed()));
    compactNavigation.addEventListener("change", (event) =>
        setOpen(!breakpoint.matches && ((event.matches && !isDashboard) || !startsCollapsed())));
})();
