const root = document.documentElement;
        try {
            const savedTheme = localStorage.getItem("mdu-theme");
            if (["auto", "light", "dark"].includes(savedTheme)) {
                root.dataset.theme = savedTheme;
            }
        } catch { }
        const systemTheme = matchMedia("(prefers-color-scheme: dark)");
        const updateColorScheme = () => {
            const scheme = root.dataset.theme === "auto"
                ? systemTheme.matches ? "dark" : "light"
                : root.dataset.theme;
            // A theme switch changes every color at once; without this, elements with a color transition (the bar
            // behind the header fades in 300 ms) would lag behind and flash a mixed tone for a moment.
            if (root.dataset.colorScheme && root.dataset.colorScheme !== scheme) {
                root.classList.add("is-switching-theme");
                requestAnimationFrame(() => requestAnimationFrame(() => root.classList.remove("is-switching-theme")));
            }
            root.dataset.colorScheme = scheme;
        };
        new MutationObserver(updateColorScheme)
            .observe(root, { attributes: true, attributeFilter: ["data-theme"] });
        systemTheme.addEventListener("change", updateColorScheme);
        updateColorScheme();
