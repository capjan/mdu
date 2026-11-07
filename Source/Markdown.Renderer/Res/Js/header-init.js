(() => {
    const root = document.documentElement;
    const panels = mduPanels;
    const header = document.getElementById("site-header");
    const navigationToggle = document.getElementById("navigation-toggle");
    const open = !panels.mobile.matches && !root.classList.contains("sidebar-starts-collapsed");
    panels.updateNavigationHeader(header, navigationToggle, open);

    const styles = getComputedStyle(root);
    // Measure the stable scrollbar gutter before long content has created viewport overflow.
    const overflow = root.style.overflowY;
    root.style.overflowY = "scroll";
    const pageWidth = Math.min(root.getBoundingClientRect().width, parseFloat(styles.getPropertyValue("--page-max-width")));
    root.style.overflowY = overflow;
    const sidebarVisible = open && (header.classList.contains("has-wide-sidebar") || panels.compact.matches);
    const measured = panels.measure(pageWidth, 2 * panels.mainPadding, sidebarVisible, document.documentElement.dataset.hasToc === "true");
    root.classList.toggle("toc-space-available", measured.tocFits);
    const tocToggle = document.getElementById("toc-toggle");
    tocToggle.hidden = !measured.tocFits;
    tocToggle.setAttribute("aria-expanded", String(!root.classList.contains("toc-collapsed")));
})();
