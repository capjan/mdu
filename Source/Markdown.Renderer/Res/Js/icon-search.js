(() => {
    const widgets = document.querySelectorAll(".icon-search");
    if (widgets.length === 0) return;

    const pageSize = 120;
    const scriptUrl = new URL(document.currentScript.src);
    const indexUrl = new URL("icon-search-index.json", scriptUrl);
    // Keep the index cache-busted together with the script that requests it.
    const indexVersion = scriptUrl.searchParams.get("i");
    if (indexVersion) indexUrl.searchParams.set("v", indexVersion);
    const numberFormat = new Intl.NumberFormat("en");
    const toSvg = (viewBox, body, className) =>
        `<svg${className ? ` class="${className}"` : ""} xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" aria-hidden="true" focusable="false">${body}</svg>`;
    let itemsPromise;
    // The index holds every icon, so it is only loaded when a search comes into view.
    const loadItems = () => itemsPromise ??= fetch(indexUrl)
        .then((response) => {
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return response.json();
        })
        .then((index) => [
            ...index.sets.flatMap((set) => set.icons.map(([name, viewBox, body, darkViewBox, darkBody]) => ({
                kind: "icons",
                name,
                shortcode: set.prefix + name,
                setName: set.name,
                svg: darkBody === undefined
                    ? toSvg(viewBox, body)
                    : toSvg(viewBox, body, "icon-light") + toSvg(darkViewBox, darkBody, "icon-dark")
            }))),
            ...index.emojis.map(([name, emoji]) => ({ kind: "emojis", name, shortcode: name, emoji }))
        ]);

    const copyText = async (text) => {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const field = document.createElement("textarea");
            field.value = text;
            field.setAttribute("readonly", "");
            field.style.position = "fixed";
            field.style.opacity = "0";
            document.body.append(field);
            field.select();
            const copied = document.execCommand("copy");
            field.remove();
            return copied;
        }
    };

    // Exact names first, then names that start with the query, then names that contain it as a word.
    const rank = (item, query) => {
        if (item.name === query || item.shortcode === query) return 0;
        if (item.name.startsWith(query) || item.shortcode.startsWith(query)) return 1;
        if (item.shortcode.includes(`-${query}`) || item.shortcode.includes(`_${query}`)) return 2;
        return 3;
    };

    const search = (items, filter, text) => {
        const terms = text.toLowerCase().replace(/:/g, " ").split(/\s+/).filter(Boolean);
        if (terms.length === 0) return [];
        const query = terms.join("-");
        return items
            .filter((item) => (filter === "all" || item.kind === filter)
                && terms.every((term) => item.shortcode.includes(term)))
            .map((item) => ({ item, rank: rank(item, query) }))
            .sort((left, right) => left.rank - right.rank
                || left.item.shortcode.length - right.item.shortcode.length
                || left.item.shortcode.localeCompare(right.item.shortcode))
            .map((result) => result.item);
    };

    const renderItem = (item) => {
        const listItem = document.createElement("li");
        const button = document.createElement("button");
        const preview = document.createElement("span");
        const code = document.createElement("code");
        const shortcode = `:${item.shortcode}:`;

        button.type = "button";
        button.className = "icon-search-result";
        button.title = item.kind === "icons" ? `${item.setName}: copy ${shortcode}` : `Emoji: copy ${shortcode}`;
        preview.className = item.kind === "icons" ? "icon" : "emoji";
        preview.setAttribute("aria-hidden", "true");
        if (item.kind === "icons") preview.innerHTML = item.svg;
        else preview.textContent = item.emoji;
        code.textContent = shortcode;
        button.dataset.shortcode = shortcode;
        button.append(preview, code);
        listItem.append(button);
        return listItem;
    };

    widgets.forEach((widget) => {
        const input = widget.querySelector(".icon-search-input");
        const filters = widget.querySelectorAll("[data-filter]");
        const status = widget.querySelector(".icon-search-status");
        const results = widget.querySelector(".icon-search-results");
        const more = widget.querySelector(".icon-search-more");
        let items;
        let filter = "all";
        let matches = [];
        let shown = 0;
        let copiedTimer;

        const summary = () => {
            const icons = items.filter((item) => item.kind === "icons").length;
            return `Search ${numberFormat.format(icons)} icons and ${numberFormat.format(items.length - icons)} emojis. Click a result to copy its shortcode.`;
        };

        const showMore = () => {
            const fragment = document.createDocumentFragment();
            matches.slice(shown, shown + pageSize).forEach((item) => fragment.append(renderItem(item)));
            results.append(fragment);
            shown = Math.min(matches.length, shown + pageSize);
            more.hidden = shown >= matches.length;
        };

        const update = () => {
            if (!items) return;
            matches = search(items, filter, input.value);
            shown = 0;
            results.replaceChildren();
            if (!input.value.trim()) status.textContent = summary();
            else if (matches.length === 0) status.textContent = `No icons or emojis match “${input.value.trim()}”.`;
            else status.textContent = `${numberFormat.format(matches.length)} ${matches.length === 1 ? "result" : "results"}`;
            showMore();
        };

        let loading = false;
        const load = () => {
            if (loading) return;
            loading = true;
            observer?.disconnect();
            loadItems().then((loadedItems) => {
                items = loadedItems;
                update();
            }, (error) => {
                status.textContent = `The icons could not be loaded (${error.message}).`;
            });
        };
        const observer = "IntersectionObserver" in window
            ? new IntersectionObserver((entries) => {
                if (entries.some((entry) => entry.isIntersecting)) load();
            }, { rootMargin: "400px" })
            : null;

        status.textContent = "Loading icons and emojis…";
        input.disabled = false;
        input.addEventListener("focus", load, { once: true });
        if (observer) observer.observe(widget);
        else load();

        input.addEventListener("input", update);
        more.addEventListener("click", showMore);
        filters.forEach((button) => button.addEventListener("click", () => {
            filter = button.dataset.filter;
            filters.forEach((other) => other.setAttribute("aria-pressed", String(other === button)));
            update();
        }));

        results.addEventListener("click", async (event) => {
            const button = event.target.closest(".icon-search-result");
            if (!button) return;
            const shortcode = button.dataset.shortcode;
            const copied = await copyText(shortcode);
            results.querySelectorAll(".is-copied").forEach((element) => element.classList.remove("is-copied"));
            if (copied) button.classList.add("is-copied");
            status.textContent = copied ? `Copied ${shortcode} to the clipboard.` : `Copy ${shortcode} manually; the clipboard is not available.`;
            clearTimeout(copiedTimer);
            copiedTimer = setTimeout(() => button.classList.remove("is-copied"), 1500);
        });
    });
})();
