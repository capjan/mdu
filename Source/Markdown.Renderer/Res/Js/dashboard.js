(() => {
    const dashboard = document.querySelector(".dashboard");
    const input = document.getElementById("dashboard-search-input");
    if (!dashboard || !input) return;

    const hint = document.getElementById("dashboard-search-hint");
    const status = document.getElementById("dashboard-search-status");
    const clearButton = document.getElementById("dashboard-search-clear");
    const pagesSection = document.getElementById("dashboard-pages");
    const pagesList = document.getElementById("dashboard-pages-list");
    const resultsGrid = document.getElementById("dashboard-results");
    const empty = document.getElementById("dashboard-empty");
    const clock = document.getElementById("dashboard-clock");
    const searchUrl = dashboard.dataset.dashboardSearch;
    const indexPath = document.body.dataset.searchIndex;
    const tiles = [...dashboard.querySelectorAll(".dashboard-tile")];
    const groups = [...dashboard.querySelectorAll(".dashboard-group")];
    const rows = [...dashboard.querySelectorAll(".dashboard-row")];
    const opensNewTab = (link) => link.target === "_blank";
    const isVisibleInResults = (element) => {
        for (let current = element; current && current !== resultsGrid; current = current.parentElement)
            if (current.hidden) return false;
        return true;
    };

    // Markdown headings can claim the same generated IDs, so keep the referenced cells unique in the final page.
    for (const [index, tile] of tiles.entries()) {
        const cell = tile.querySelector('[role="gridcell"]');
        if (document.getElementById(cell.id) !== cell || document.querySelectorAll(`[id="${CSS.escape(cell.id)}"]`).length > 1) {
            let suffix = index;
            while (document.getElementById(`dashboard-result-tile-${suffix}`)) suffix++;
            cell.id = `dashboard-result-tile-${suffix}`;
        }
    }

    const fold = (text) => text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase().replace(/ß/g, "ss");
    const tokenize = (text) => fold(text).match(/[\p{L}\p{N}_]+/gu) || [];

    const tileEntries = tiles.map(tile => {
        const link = tile.querySelector("a");
        const group = tile.closest(".dashboard-group")?.querySelector("h2")?.textContent || "";
        return {
            tile,
            link,
            name: fold(tile.querySelector(".dashboard-name")?.textContent || ""),
            text: fold(`${tile.textContent} ${link.href} ${group}`)
        };
    });

    // The page an index entry belongs to; section entries carry an anchor.
    const pageOf = (entry) => entry.url.split("#")[0];
    let pageEntries;
    let indexRequest;
    const loadIndex = () => {
        if (!indexPath) return Promise.resolve();
        indexRequest ??= fetch(indexPath, { cache: "no-cache" })
            .then(response => response.ok ? response.json() : [])
            .then(entries => {
                const pagePath = url => url.pathname.replace(/(?:index|README)\.html$/i, "");
                const currentPage = pagePath(new URL(window.location.href));
                const indexUrl = new URL(indexPath, window.location.href);
                pageEntries = entries.filter(entry => pagePath(new URL(pageOf(entry), indexUrl)) !== currentPage);
            if (input.value.trim()) update();
            })
            .catch(() => {
                pageEntries = [];
                if (input.value.trim()) update();
            });
        return indexRequest;
    };

    const rankPages = (terms) => {
        if (!pageEntries || terms.length === 0) return [];
        const scored = pageEntries.map(entry => {
            const title = fold(entry.title);
            const section = fold(entry.section);
            const all = `${title} ${section} ${fold(entry.text)}`;
            if (!terms.every(term => all.includes(term))) return null;
            const score = terms.reduce((total, term) => total + (title.includes(term) ? 30 : section.includes(term) ? 15 : 1), 0);
            return { entry, score };
        }).filter(Boolean);

        // The index holds one entry per section; a page appears once, with its best matching section.
        const pages = new Map();
        for (const result of scored) {
            const page = pages.get(pageOf(result.entry));
            if (!page) pages.set(pageOf(result.entry), { ...result, matches: 1 });
            else {
                page.matches++;
                if (result.score > page.score) Object.assign(page, { entry: result.entry, score: result.score });
            }
        }

        return [...pages.values()]
            .sort((left, right) => right.score - left.score || right.matches - left.matches ||
                left.entry.title.localeCompare(right.entry.title))
            .slice(0, 4);
    };

    const webSearchModifier = navigator.platform.includes("Mac") ? "option" : "alt";
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    // The same markup as ++enter++ in Markdown, so the hints show the keycaps of the Keyboard Keys feature.
    const keyDefinitions = {
        enter: { label: "Enter", symbol: "⏎" },
        option: { label: "Option", symbol: "⌥" },
        alt: { label: "Alt" },
        escape: { label: "Esc", symbol: "⎋" },
        "arrow-up": { label: "Up", symbol: "↑", symbolOnly: true },
        "arrow-down": { label: "Down", symbol: "↓", symbolOnly: true }
    };

    const createKeys = (names) => {
        const keys = document.createElement("span");
        keys.className = "keys";
        names.forEach((name, index) => {
            if (index > 0) {
                const plus = document.createElement("span");
                plus.textContent = "+";
                keys.append(plus);
            }
            const definition = keyDefinitions[name];
            const kbd = document.createElement("kbd");
            kbd.className = `key-${name}`;
            if (definition.symbol) {
                const symbol = document.createElement("span");
                symbol.className = "key-symbol";
                symbol.setAttribute("aria-hidden", "true");
                symbol.textContent = definition.symbol;
                kbd.append(symbol);
            }
            const label = document.createElement("span");
            label.className = definition.symbolOnly ? "visually-hidden" : "key-label";
            label.textContent = definition.label;
            kbd.append(label);
            keys.append(kbd);
        });
        return keys;
    };

    // Each hint lists its shortcuts and what they do, for example [[["enter"]], "open GitHub"].
    const setHints = (...hints) => {
        hint.replaceChildren();
        for (const [shortcuts, text] of hints) {
            const item = document.createElement("span");
            item.className = "dashboard-search-hint-item";
            for (const names of shortcuts) item.append(createKeys(names));
            item.append(` ${text}`);
            hint.append(item);
        }
    };

    // The selected tile or page is what Enter opens; the arrow keys move the selection.
    let selected = null;
    const selectable = () => [...dashboard.querySelectorAll(".dashboard-tile:not([hidden]) a.dashboard-link, a.dashboard-page")]
        .filter(isVisibleInResults);

    // The hints name actions, not results, so the row keeps its width while the arrow keys move the selection.
    const renderHints = (items = selectable()) => {
        const query = input.value.trim();
        const hints = [];
        if (selected) hints.push([[["enter"]], "open"]);
        else if (query && searchUrl) hints.push([[["enter"]], "search the web"]);
        if (selected && items.length > 1) hints.push([[["arrow-up"], ["arrow-down"]], "select"]);
        if (selected && query && searchUrl) hints.push([[[webSearchModifier, "enter"]], "search the web"]);
        if (query) hints.push([[["escape"]], "clear"]);
        setHints(...hints);
    };

    const select = (link, reveal = false, items) => {
        if (selected !== link && selected) {
            selected.classList.remove("is-selected");
            selected.closest('[role="gridcell"]').setAttribute("aria-selected", "false");
        }
        selected = link;
        link?.classList.add("is-selected");
        if (link) {
            const cell = link.closest('[role="gridcell"]');
            cell.setAttribute("aria-selected", "true");
            input.setAttribute("aria-activedescendant", cell.id);
        } else {
            input.removeAttribute("aria-activedescendant");
        }
        if (link && reveal) link.scrollIntoView({ block: "nearest", inline: "nearest", behavior: prefersReducedMotion.matches ? "auto" : "smooth" });
        renderHints(items);
    };

    // Starts the excerpt shortly before the first match so that the sheet shows why the page matched.
    const createExcerpt = (text, terms) => {
        const folded = fold(text);
        const offsets = terms.map(term => folded.indexOf(term)).filter(offset => offset >= 0);
        const first = offsets.length === 0 ? 0 : Math.min(...offsets);
        // Folding can change the length of the text, so the offset is only a close estimate for the original text.
        const start = first > 60 ? text.lastIndexOf(" ", first - 40) + 1 : 0;
        const excerpt = text.slice(start, start + 420);
        return `${start > 0 ? "…" : ""}${excerpt}${start + 420 < text.length ? "…" : ""}`;
    };

    const appendHighlighted = (container, text, terms) => {
        const pattern = terms
            .map(term => term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
            .join("|");
        if (!pattern) {
            container.append(text);
            return;
        }

        let offset = 0;
        for (const match of text.matchAll(new RegExp(pattern, "giu"))) {
            container.append(text.slice(offset, match.index));
            const mark = document.createElement("mark");
            mark.textContent = match[0];
            container.append(mark);
            offset = match.index + match[0].length;
        }
        container.append(text.slice(offset));
    };

    const siteName = document.getElementById("site-brand-title")?.textContent.trim() || "Home";
    // The folder a page lives in, for example "Architecture" for Architecture/Rendering.html.
    const folderOf = (pageUrl) => {
        const segments = pageUrl.split("/").slice(0, -1);
        if (/^(?:index|README)\.html$/i.test(pageUrl.split("/").pop())) segments.pop();
        const folder = segments.pop();
        return folder ? decodeURIComponent(folder).replace(/[-_]+/g, " ") : siteName;
    };

    const documentIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 1.5h5.5L13 5v9.5H4z"></path><path d="M9.5 1.5V5H13"></path></svg>';
    const arrowIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3 8h9.5M8.5 4l4 4-4 4"></path></svg>';
    const pageIds = new Map();
    const pageId = (page) => {
        if (!pageIds.has(page)) {
            const suffix = pageIds.size;
            let id = `dashboard-page-${suffix}`;
            let attempt = 0;
            while (document.getElementById(id)) id = `dashboard-page-${suffix}-${++attempt}`;
            pageIds.set(page, id);
        }
        return pageIds.get(page);
    };
    let shownPages = new Set();

    const renderPages = (results, terms) => {
        pagesList.replaceChildren();
        pagesSection.hidden = results.length === 0;
        const previousPages = shownPages;
        shownPages = new Set(results.map(({ entry }) => pageOf(entry)));
        if (results.length === 0) return;

        const indexUrl = new URL(indexPath, window.location.href);
        results.forEach(({ entry, matches }, index) => {
            const item = document.createElement("li");
            item.setAttribute("role", "row");
            const cell = document.createElement("div");
            const page = pageOf(entry);
            cell.id = pageId(page);
            cell.setAttribute("role", "gridcell");
            cell.setAttribute("aria-selected", "false");
            cell.setAttribute("aria-label", entry.section && entry.section !== entry.title
                ? `${entry.title}: ${entry.section}` : entry.title);
            const link = document.createElement("a");
            link.className = "dashboard-page";
            // Only sheets that were not shown before slide in, so typing does not replay the animation.
            if (!previousPages.has(pageOf(entry))) {
                link.classList.add("is-entering");
                link.style.setProperty("--dashboard-page-index", String(index));
                link.addEventListener("animationend", () => link.classList.remove("is-entering"), { once: true });
            }
            link.href = new URL(entry.url, indexUrl).href;

            const label = document.createElement("span");
            label.className = "dashboard-page-label";
            label.innerHTML = documentIcon;
            const labelText = document.createElement("span");
            labelText.textContent = folderOf(pageOf(entry));
            label.append(labelText);

            const title = document.createElement("strong");
            title.className = "dashboard-page-title";
            title.textContent = entry.title;
            link.append(label, title);

            const hasSection = entry.section && entry.section !== entry.title;
            if (hasSection) {
                const section = document.createElement("span");
                section.className = "dashboard-page-section";
                section.textContent = entry.section;
                link.append(section);
            }

            const text = document.createElement("span");
            text.className = "dashboard-page-text";
            appendHighlighted(text, createExcerpt(entry.text, terms), terms);

            const footer = document.createElement("span");
            footer.className = "dashboard-page-footer";
            const more = document.createElement("span");
            more.textContent = matches > 1 ? `${matches} matching sections` : "";
            footer.append(more);
            footer.insertAdjacentHTML("beforeend", arrowIcon);

            link.append(text, footer);
            cell.append(link);
            item.append(cell);
            pagesList.append(item);
        });
    };

    const update = () => {
        const query = input.value.trim();
        const terms = tokenize(query);
        let first = null;

        for (const entry of tileEntries) {
            const matches = terms.every(term => entry.text.includes(term));
            entry.tile.hidden = !matches;
            if (matches && terms.length > 0) {
                const startsWith = terms.length > 0 && entry.name.startsWith(terms[0]);
                if (!first || (startsWith && !first.startsWith)) first = { ...entry, startsWith };
            }
        }
        for (const group of groups) group.hidden = !group.querySelector(".dashboard-tile:not([hidden]), .dashboard-note") && terms.length > 0;
        for (const row of rows) row.hidden = !row.querySelector(".dashboard-group:not([hidden])") && terms.length > 0;

        const pages = rankPages(terms);
        // Clear the reference before replacing page cells, including when the new result has the same ID.
        if (selected?.classList.contains("dashboard-page")) {
            selected.classList.remove("is-selected");
            selected.closest('[role="gridcell"]')?.setAttribute("aria-selected", "false");
            selected = null;
            input.removeAttribute("aria-activedescendant");
        }
        renderPages(pages, terms);

        const items = selectable();
        select(terms.length === 0 ? null : first?.link ?? pagesList.querySelector("a.dashboard-page"), false, items);
        resultsGrid.hidden = ![...resultsGrid.querySelectorAll('[role="rowgroup"]')].some(isVisibleInResults);
        input.setAttribute("aria-expanded", String(!resultsGrid.hidden));

        const count = items.length;
        const waitingForIndex = terms.length > 0 && !!indexPath && !pageEntries;
        const message = terms.length === 0
            ? `${count} ${count === 1 ? "link" : "links"}.`
            : count === 0
                ? searchUrl
                    ? `No links or pages match “${query}”. Press Enter to search the web.`
                    : `No links or pages match “${query}”.`
                : `${count} ${count === 1 ? "match" : "matches"} for “${query}”.`;
        status.setAttribute("aria-busy", String(waitingForIndex));
        if (waitingForIndex) status.textContent = "";
        else if (status.textContent !== message) status.textContent = message;

        empty.hidden = !(terms.length > 0 && !first && pages.length === 0);
        empty.textContent = searchUrl
            ? `No links or pages match “${query}”. Press Enter to search the web.`
            : `No links or pages match “${query}”.`;
    };

    // Pointing at a tile or page selects it, so the mouse and the arrow keys share one selection and Enter opens what
    // the pointer is on. Only real pointer movement counts, so results that appear under a resting pointer while
    // typing keep the best match selected.
    const pointerTarget = (event) => event.target instanceof Element
        ? event.target.closest("a.dashboard-link, a.dashboard-page")
        : null;

    dashboard.addEventListener("pointermove", event => {
        if (event.pointerType !== "mouse") return;
        const link = pointerTarget(event);
        if (!link) return;
        const bounds = link.getBoundingClientRect();
        link.style.setProperty("--dashboard-pointer-x", `${event.clientX - bounds.left}px`);
        link.style.setProperty("--dashboard-pointer-y", `${event.clientY - bounds.top}px`);
        if (link !== selected) select(link);
    });

    // Without a filter there is no best match to return to, so the selection ends with the pointer leaving the tile.
    dashboard.addEventListener("pointerout", event => {
        const link = pointerTarget(event);
        if (!link || link !== selected || input.value.trim()) return;
        if (event.relatedTarget instanceof Node && link.contains(event.relatedTarget)) return;
        select(null);
    });

    const clear = () => {
        input.value = "";
        update();
        input.focus();
    };

    clearButton.addEventListener("click", clear);
    input.addEventListener("focus", () => loadIndex(), { once: true });
    input.addEventListener("input", () => {
        update();
        if (input.value.trim() && !pageEntries) loadIndex();
    });

    input.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            if (input.value) {
                clear();
            } else {
                input.blur();
            }
            return;
        }

        if ((event.key === "ArrowDown" || event.key === "ArrowUp") && !event.isComposing) {
            const items = selectable();
            if (items.length === 0) return;
            event.preventDefault();
            const step = event.key === "ArrowDown" ? 1 : -1;
            const index = items.indexOf(selected);
            const next = index < 0
                ? step > 0 ? 0 : items.length - 1
                : (index + step + items.length) % items.length;
            select(items[next], true, items);
            return;
        }

        if (event.key !== "Enter" || event.isComposing) return;
        const query = input.value.trim();
        if (!query && !selected) return;
        event.preventDefault();
        const newTab = event.metaKey || event.ctrlKey;
        if (selected && !(event.altKey && searchUrl && query)) {
            if (newTab || opensNewTab(selected)) window.open(selected.href, "_blank", "noopener");
            else window.location.href = selected.href;
        } else if (searchUrl && query) {
            const url = searchUrl.replace("{query}", encodeURIComponent(query));
            if (newTab) window.open(url, "_blank", "noopener");
            else window.location.href = url;
        }
    });

    // On a dashboard, the slash key focuses the dashboard filter instead of opening the site search.
    window.addEventListener("keydown", event => {
        const isTyping = event.target instanceof HTMLElement &&
            (event.target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName));
        if (event.key === "/" && !isTyping && !event.metaKey && !event.ctrlKey) {
            event.preventDefault();
            event.stopImmediatePropagation();
            input.focus();
            input.select();
        }
    }, true);

    // Like a browser start page, the filter is ready for typing; touch devices skip this to keep the keyboard closed.
    if (!window.location.hash && window.matchMedia("(pointer: fine)").matches) input.focus({ preventScroll: true });

    if (clock) {
        const time = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" });
        const date = new Intl.DateTimeFormat(undefined, { weekday: "long", day: "numeric", month: "long" });
        const tick = () => {
            const now = new Date();
            const strong = document.createElement("strong");
            strong.textContent = time.format(now);
            clock.replaceChildren(strong, ` ${date.format(now)}`);
            clock.dateTime = now.toISOString();
            setTimeout(tick, 60000 - now.getSeconds() * 1000 - now.getMilliseconds() + 50);
        };
        tick();
    }

    if (dashboard.dataset.dashboardStatus === "true") {
        const check = async (link, status) => {
            const url = new URL(link.href);
            if (window.location.protocol === "https:" && url.protocol === "http:") return;
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 6000);
            try {
                await fetch(url, { mode: "no-cors", cache: "no-store", signal: controller.signal });
                status.classList.add("is-up");
                status.setAttribute("aria-label", "Reachable");
            } catch {
                status.classList.add("is-down");
                status.setAttribute("aria-label", "Not reachable");
            } finally {
                clearTimeout(timeout);
            }
        };
        for (const status of dashboard.querySelectorAll(".dashboard-status"))
            check(status.closest("a"), status);
    }
})();
