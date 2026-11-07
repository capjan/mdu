const root = document.documentElement;
        try {
            const savedTheme = localStorage.getItem("mdu-theme");
            if (["auto", "light", "dark"].includes(savedTheme)) {
                root.dataset.theme = savedTheme;
            }
        } catch { }
        const systemTheme = matchMedia("(prefers-color-scheme: dark)");
        const updateColorScheme = () => {
            root.dataset.colorScheme = root.dataset.theme === "auto"
                ? systemTheme.matches ? "dark" : "light"
                : root.dataset.theme;
        };
        new MutationObserver(updateColorScheme)
            .observe(root, { attributes: true, attributeFilter: ["data-theme"] });
        systemTheme.addEventListener("change", updateColorScheme);
        updateColorScheme();
