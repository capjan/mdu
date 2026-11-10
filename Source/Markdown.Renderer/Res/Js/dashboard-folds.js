(() => {
    // Runs inline right after the groups, before the page is painted: folding starts only with a script that can undo it,
    // and the reader's saved choices apply without a visible jump. dashboard.js reads and writes the same entry.
    const dashboard = document.currentScript?.closest(".dashboard");
    if (!dashboard) return;
    dashboard.classList.add("is-folding");
    // A folder's start page has two addresses, such as /wiki/ and /wiki/index.html; both share one entry.
    dashboard.dataset.foldKey = `mdu-dashboard-folds:${window.location.pathname.replace(/(?:index|README)\.html$/i, "")}`;
    const groups = [...dashboard.querySelectorAll(".dashboard-group")];
    // The page leaves the labels disabled and every group open, which is true without a script; folding starts here.
    for (const group of groups) {
        const toggle = group.querySelector(".dashboard-group-toggle");
        if (!toggle) continue;
        toggle.disabled = false;
        toggle.setAttribute("aria-expanded", String(!group.classList.contains("is-collapsed")));
    }

    const apply = (group, collapsed, expanded) => {
        group.classList.toggle("is-collapsed", collapsed);
        group.classList.toggle("is-expanded", expanded);
        group.querySelector(".dashboard-group-toggle")?.setAttribute("aria-expanded", String(!collapsed));
        const more = group.querySelector(".dashboard-more");
        if (!more) return;
        more.setAttribute("aria-expanded", String(expanded));
        more.textContent = expanded ? "Show fewer" : `Show ${more.dataset.more} more`;
    };

    let folds = {};
    try {
        folds = JSON.parse(localStorage.getItem(dashboard.dataset.foldKey) || "{}") || {};
    } catch {
        // Without storage, the groups start as the page defines them.
    }
    groups.forEach((group, index) => {
        const saved = folds[group.querySelector("h2")?.id || String(index)];
        if (saved) apply(group, !!saved.collapsed, !!saved.expanded);
    });

    // A link to a group or to anything inside it opens the group in full, without changing the saved choice, so the
    // browser can scroll to the target. dashboard.js shows the tiles again when the fragment changes later.
    const reveal = () => {
        let target = null;
        try {
            target = window.location.hash ? document.getElementById(decodeURIComponent(window.location.hash.slice(1))) : null;
        } catch {
            return null;
        }
        const group = target?.closest(".dashboard-group");
        if (!group || !dashboard.contains(group)) return null;
        apply(group, false, true);
        return target;
    };
    reveal();
    window.addEventListener("hashchange", () => {
        const target = reveal();
        if (target) dashboard.dispatchEvent(new CustomEvent("dashboard-reveal", { detail: target }));
    });
})();
