(() => {
    // Runs inline right after the groups, before the page is painted: folding starts only with a script that can undo it,
    // and the reader's saved choices apply without a visible jump. dashboard.js reads and writes the same entry.
    const dashboard = document.currentScript?.closest(".dashboard");
    if (!dashboard) return;
    dashboard.classList.add("is-folding");
    // A folder's start page has two addresses, such as /wiki/ and /wiki/index.html; both share one entry.
    const page = window.location.pathname.replace(/(?:index|README)\.html$/i, "");
    dashboard.dataset.foldKey = `mdu-dashboard-folds:${page}`;
    const groups = [...dashboard.querySelectorAll(".dashboard-group:not(.dashboard-frequent)")];

    // The view switch shows only with a script; the reader's choice of view overrides the page's start view.
    dashboard.dataset.viewKey = `mdu-dashboard-view:${page}`;
    const view = document.getElementById("dashboard-view");
    if (view && dashboard.contains(view)) {
        try {
            const saved = localStorage.getItem(dashboard.dataset.viewKey);
            if (saved) dashboard.classList.toggle("is-compact", saved === "compact");
        } catch {
            // Without storage, the page's start view applies.
        }
        view.setAttribute("aria-pressed", String(dashboard.classList.contains("is-compact")));
        view.hidden = false;
    }
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

    // The row of frequently used links shows copies of the tiles this reader opened at least twice, those opened often and
    // lately first; a use counts half after 30 days. dashboard.js counts the uses in the same entry.
    const frequent = dashboard.querySelector(".dashboard-frequent");
    if (frequent) {
        dashboard.dataset.frequentKey = `mdu-dashboard-frequent:${page}`;
        let uses = {};
        try {
            uses = JSON.parse(localStorage.getItem(dashboard.dataset.frequentKey) || "{}") || {};
        } catch {
            // Without storage, the row stays hidden.
        }
        const links = new Map();
        for (const group of groups)
            for (const link of group.querySelectorAll(".dashboard-tile a.dashboard-link"))
                if (!links.has(link.href)) links.set(link.href, link);
        const now = Date.now();
        const score = use => use.n * 0.5 ** ((now - use.t) / (30 * 864e5));
        const picks = Object.entries(uses)
            .filter(([href, use]) => use?.n >= 2 && links.has(href))
            .sort(([, a], [, b]) => score(b) - score(a))
            .slice(0, Number(frequent.dataset.frequent) || 6);
        const list = frequent.querySelector(".dashboard-tiles");
        picks.forEach(([href], index) => {
            const link = links.get(href);
            const tile = link.closest(".dashboard-tile").cloneNode(true);
            tile.classList.remove("is-overflow");
            tile.hidden = false;
            tile.querySelector(".dashboard-tile-more")?.remove();
            const cell = tile.querySelector('[role="gridcell"]');
            let suffix = index;
            while (document.getElementById(`dashboard-frequent-tile-${suffix}`)) suffix++;
            cell.id = `dashboard-frequent-tile-${suffix}`;
            list.append(tile);
        });
        frequent.parentElement.hidden = picks.length === 0;
    }

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
