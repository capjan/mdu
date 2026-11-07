(() => {
        const navigation = document.getElementById("site-navigation");
        const currentPage = navigation.querySelector('.site-nav-link[aria-current="page"]');
        navigation.querySelectorAll(".site-nav-toggle").forEach((toggle) => {
            const children = document.getElementById(toggle.getAttribute("aria-controls"));
            const link = toggle.parentElement.querySelector(".site-nav-link");
            const storageKey = `mdu-nav-expanded:${new URL(link.href).pathname}`;
            const onCurrentPath = link === currentPage || !!currentPage && children.contains(currentPage);
            let saved = null;
            try {
                saved = sessionStorage.getItem(storageKey);
            } catch { }
            const expanded = onCurrentPath ||
                (saved === null ? toggle.getAttribute("aria-expanded") === "true" : saved === "true");
            const setExpanded = (open) => {
                children.hidden = !open;
                toggle.setAttribute("aria-expanded", String(open));
                toggle.setAttribute("aria-label", `${open ? "Collapse" : "Expand"} ${link.textContent.trim()}`);
            };
            setExpanded(expanded);
            if (expanded && onCurrentPath) {
                try { sessionStorage.setItem(storageKey, "true"); } catch { }
            }
            toggle.addEventListener("click", () => {
                const open = toggle.getAttribute("aria-expanded") !== "true";
                setExpanded(open);
                try { sessionStorage.setItem(storageKey, String(open)); } catch { }
            });
        });
    })();
