(() => {
    // Runs inline right after the groups, before the page is painted: folding starts only with a script that can undo it,
    // and the reader's saved choices apply without a visible jump. dashboard.js reads and writes the same entry.
    const dashboard = document.currentScript?.closest(".dashboard");
    if (!dashboard) return;
    dashboard.classList.add("is-folding");
    let folds = {};
    try {
        folds = JSON.parse(localStorage.getItem(`mdu-dashboard-folds:${window.location.pathname}`) || "{}") || {};
    } catch {
        return;
    }
    [...dashboard.querySelectorAll(".dashboard-group")].forEach((group, index) => {
        const saved = folds[group.querySelector("h2")?.id || String(index)];
        if (!saved) return;
        group.classList.toggle("is-collapsed", !!saved.collapsed);
        group.classList.toggle("is-expanded", !!saved.expanded);
        group.querySelector(".dashboard-group-toggle")?.setAttribute("aria-expanded", String(!saved.collapsed));
        const more = group.querySelector(".dashboard-more");
        if (!more) return;
        more.setAttribute("aria-expanded", String(!!saved.expanded));
        if (saved.expanded) more.textContent = "Show fewer";
    });
})();
