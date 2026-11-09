(() => {
    const searchButton = document.getElementById("search-button");
    const searchDialog = document.getElementById("search-dialog");
    const searchInput = document.getElementById("search-input");
    const searchStatus = document.getElementById("search-status");
    const searchResults = document.getElementById("search-results");
    const shortcut = document.getElementById("search-shortcut");
    const indexPath = document.body.dataset.searchIndex;
    let entries;

    if (!navigator.platform.includes("Mac")) shortcut.textContent = "Ctrl K";

    const fold = (text) => text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase().replace(/ß/g, "ss");
    const tokenize = (text) => fold(text).match(/[\p{L}\p{N}_]+(?:[+#][\p{L}\p{N}_]*)*/gu) || [];

    const mapText = (text) => {
        let value = "";
        const positions = [];
        let offset = 0;

        for (const character of text) {
            const start = offset;
            const end = start + character.length;
            offset = end;

            for (const foldedCharacter of fold(character)) {
                value += foldedCharacter;
                positions.push({ start, end });
            }
        }

        return { value, positions };
    };

    const getRanges = (text, terms) => {
        const mapped = mapText(text);
        const ranges = [];

        for (const term of terms) {
            const foldedTerm = fold(term);
            if (!foldedTerm) continue;

            let offset = 0;
            while ((offset = mapped.value.indexOf(foldedTerm, offset)) !== -1) {
                const first = mapped.positions[offset];
                const last = mapped.positions[offset + foldedTerm.length - 1];
                ranges.push({ start: first.start, end: last.end });
                offset += foldedTerm.length;
            }
        }

        ranges.sort((left, right) => left.start - right.start || left.end - right.end);
        return ranges.reduce((merged, range) => {
            const previous = merged[merged.length - 1];
            if (previous && range.start <= previous.end) previous.end = Math.max(previous.end, range.end);
            else merged.push({ ...range });
            return merged;
        }, []);
    };

    const appendHighlightedText = (container, text, terms) => {
        let offset = 0;
        for (const range of getRanges(text, terms)) {
            container.append(document.createTextNode(text.slice(offset, range.start)));
            const mark = document.createElement("mark");
            mark.className = "search-highlight";
            mark.textContent = text.slice(range.start, range.end);
            container.append(mark);
            offset = range.end;
        }
        container.append(document.createTextNode(text.slice(offset)));
    };

    const createSnippet = (text, terms) => {
        const mapped = mapText(text);
        const matches = terms
            .map(term => mapped.value.indexOf(fold(term)))
            .filter(offset => offset >= 0);
        const firstMatch = matches.length === 0 ? null : mapped.positions[Math.min(...matches)]?.start;
        const start = firstMatch === null || firstMatch === undefined ? 0 : Math.max(0, firstMatch - 65);
        const end = Math.min(text.length, start + 180);
        return `${start > 0 ? "…" : ""}${text.slice(start, end)}${end < text.length ? "…" : ""}`;
    };

    const rankEntries = (query) => {
        const terms = tokenize(query);
        if (terms.length === 0) return [];

        return entries.map(entry => {
            const title = fold(entry.title);
            const section = fold(entry.section);
            const text = fold(entry.text);
            const code = fold(entry.code || "");
            const tags = fold((entry.tags || []).join(" "));
            const allText = `${title} ${section} ${text} ${code} ${tags}`;
            if (!terms.every(term => allText.includes(term))) return null;

            let score = terms.reduce((total, term) =>
                total + (title.includes(term) ? 30 : section.includes(term) ? 15 : tags.includes(term) ? 10 : 1), 0);
            // A query that starts the name of a tag or one of its aliases lists the tag page first.
            if (entry.kind === "tag") {
                const names = tokenize(`${entry.title} ${entry.code || ""}`);
                if (terms.every(term => names.some(name => name.startsWith(term)))) score += 1000;
            }
            return { entry, terms, score };
        }).filter(Boolean)
            .sort((left, right) => right.score - left.score || left.entry.title.localeCompare(right.entry.title));
    };

    const renderResults = () => {
        searchResults.replaceChildren();
        const query = searchInput.value.trim();
        if (!query) {
            searchStatus.textContent = "Start typing to search the documentation.";
            return;
        }

        if (!entries) {
            searchStatus.textContent = "Loading search index…";
            return;
        }

        const results = rankEntries(query);
        if (results.length === 0) {
            searchStatus.textContent = `No results for “${query}”.`;
            return;
        }

        searchStatus.textContent = results.length > 12
            ? `Showing 12 of ${results.length} results`
            : `${results.length} ${results.length === 1 ? "result" : "results"}`;
        const indexUrl = new URL(indexPath, window.location.href);

        for (const { entry, terms } of results.slice(0, 12)) {
            const item = document.createElement("li");
            const link = document.createElement("a");
            link.className = "search-result";
            const heading = document.createElement("strong");
            const snippet = document.createElement("span");
            snippet.className = "search-result-snippet";
            if (entry.kind === "tag") {
                const kind = document.createElement("span");
                kind.className = "search-result-kind";
                kind.textContent = "Tag: ";
                heading.append(kind);
                appendHighlightedText(heading, entry.title, terms);
                const pages = `${entry.count} ${entry.count === 1 ? "page" : "pages"}`;
                appendHighlightedText(snippet, entry.text ? `${pages} · ${createSnippet(entry.text, terms)}` : pages, terms);
                link.append(heading, snippet);
            } else {
                appendHighlightedText(heading, entry.section && entry.section !== entry.title
                    ? `${entry.title} › ${entry.section}`
                    : entry.title, terms);
                appendHighlightedText(snippet, createSnippet(entry.text, terms), terms);
                link.append(heading, snippet);
                if (entry.tags?.length) {
                    const tags = document.createElement("span");
                    tags.className = "search-result-tags";
                    appendHighlightedText(tags, `Tags: ${entry.tags.join(" · ")}`, terms);
                    link.append(tags);
                }
            }

            const resultUrl = new URL(entry.url, indexUrl);
            link.href = resultUrl.href;
            link.addEventListener("click", () => searchDialog.close());
            item.append(link);
            searchResults.append(item);
        }
    };

    const loadIndex = async () => {
        const response = await fetch(indexPath, { cache: "no-cache" });
        if (!response.ok) throw new Error(`Search index request failed: ${response.status}`);
        entries = await response.json();
    };

    const openSearch = async () => {
        searchDialog.showModal();
        searchInput.focus();
        try {
            await loadIndex();
            renderResults();
        } catch {
            searchStatus.textContent = "Search is unavailable right now.";
        }
    };

    searchButton.addEventListener("click", openSearch);
    searchInput.addEventListener("input", renderResults);
    document.getElementById("search-close").addEventListener("click", () => searchDialog.close());
    searchDialog.addEventListener("click", event => {
        if (event.target === searchDialog) searchDialog.close();
    });

    document.addEventListener("keydown", event => {
        const isTyping = event.target instanceof HTMLElement &&
            (event.target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName));
        if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            if (!searchDialog.open) openSearch();
        } else if (event.key === "/" && !isTyping && !searchDialog.open) {
            event.preventDefault();
            openSearch();
        }
    });

})();
