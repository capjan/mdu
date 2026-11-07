const mduPanels = (() => {
            const root = document.documentElement;
            root.classList.add("panels-initializing");
            const styles = getComputedStyle(root);
            const pixels = (property) => parseFloat(styles.getPropertyValue(property));
            const createPanel = (name) => ({
                key: `mdu-${name}-width`, property: `--${name}-preferred-width`,
                min: pixels(`--${name}-min-width`), max: pixels(`--${name}-max-width`),
                initial: pixels(`--${name}-preferred-width`)
            });
            const panels = {
                sidebar: createPanel("sidebar"),
                toc: createPanel("toc"),
                mobile: matchMedia("(max-width: 760px)"),
                compact: matchMedia("(max-width: 1220px)"),
                sidebarPreferenceKey: "mdu-sidebar-collapsed",
                contentMin: pixels("--panel-content-min"), tocContentMin: pixels("--toc-content-min"),
                gap: pixels("--panel-gap"), mainPadding: pixels("--main-padding"), tocThreshold: pixels("--toc-threshold")
            };
            for (const panel of [panels.sidebar, panels.toc]) {
                let width = panel.initial;
                try {
                    const saved = localStorage.getItem(panel.key);
                    if (saved !== null && Number.isFinite(Number(saved))) width = Number(saved);
                } catch { }
                root.style.setProperty(panel.property, `${Math.max(panel.min, Math.min(panel.max, width))}px`);
            }
            try {
                root.classList.toggle("toc-collapsed", localStorage.getItem("mdu-toc-collapsed") === "true");
            } catch { }
            panels.startsCollapsed = () => {
                if (document.documentElement.dataset.dashboard === "true") return true;
                try {
                    return sessionStorage.getItem(panels.sidebarPreferenceKey) === "true";
                } catch { return false; }
            };
            root.classList.toggle("sidebar-starts-collapsed", panels.startsCollapsed());
            panels.updateNavigationHeader = (header, toggle, open) => {
                header.classList.toggle("navigation-is-open", open);
                header.classList.toggle("navigation-is-collapsed", !open);
                toggle.hidden = !panels.compact.matches && !header.classList.contains("has-wide-sidebar");
                toggle.setAttribute("aria-expanded", String(open));
                const label = `${open ? "Collapse" : "Expand"} navigation`;
                toggle.setAttribute("aria-label", label);
                toggle.title = label;
            };
            panels.measure = (width, padding, sidebarVisible, hasToc) => {
                // Decide fit independently of the current sidebar width to avoid a circular TOC reserve.
                const tocFits = hasToc && !panels.mobile.matches &&
                    width - (sidebarVisible ? panels.sidebar.min : 0) - padding >= panels.tocThreshold;
                const reserve = (tocFits && !root.classList.contains("toc-collapsed")
                    ? Math.max(panels.tocThreshold, panels.tocContentMin + panels.gap + parseFloat(root.style.getPropertyValue(panels.toc.property)))
                    : panels.contentMin) + padding;
                const sidebarMax = Math.round(Math.max(panels.sidebar.min, Math.min(panels.sidebar.max, width - reserve)));
                return { tocFits, sidebarMax };
            };
            return panels;
        })();
