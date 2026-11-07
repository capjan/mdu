(() => {
    const updateScrollableTable = (wrapper) => {
        if (wrapper.scrollWidth > wrapper.clientWidth) {
            const headers = wrapper.querySelector("thead")?.innerText.replace(/\s+/g, " ").trim();
            wrapper.setAttribute("tabindex", "0");
            wrapper.setAttribute("role", "region");
            wrapper.setAttribute("aria-label", headers ? `Scrollable table: ${headers}` : "Scrollable table");
        } else {
            wrapper.removeAttribute("tabindex");
            wrapper.removeAttribute("role");
            wrapper.removeAttribute("aria-label");
        }
    };

    const observer = new ResizeObserver((entries) => {
        entries.forEach(({ target }) => {
            const wrapper = target.closest(".table-wrapper");
            if (wrapper) updateScrollableTable(wrapper);
        });
    });
    document.querySelectorAll(".table-wrapper").forEach((wrapper) => {
        observer.observe(wrapper);
        const table = wrapper.querySelector("table");
        if (table) observer.observe(table);
        updateScrollableTable(wrapper);
    });
})();
