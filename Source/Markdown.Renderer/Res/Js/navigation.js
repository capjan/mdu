(() => {
    const script = document.currentScript;
    const list = script.parentElement;
    const url = value => new URL(value, document.baseURI).href;
    const current = script.dataset.current ? url(script.dataset.current) : null;
    const sections = new Set(JSON.parse(script.dataset.sections).map(url));
    const topUrl = script.dataset.top ? url(script.dataset.top) : null;
    const prefix = script.dataset.prefix;
    const content = document.createElement("template");
    content.innerHTML = %%NAVIGATION_HTML%%;
    const links = [...content.content.querySelectorAll(".site-nav-link")];
    let top = null;
    for (const link of links) {
        link.setAttribute("href", prefix + link.getAttribute("href"));
        const address = url(link.getAttribute("href"));
        const item = link.closest(".site-nav-item");
        const isCurrent = address === current;
        const isSection = sections.has(address);
        if (isSection) item.classList.add("is-current-section");
        if (isCurrent || isSection) {
            const state = isCurrent ? "is-current" : "is-current-section";
            link.classList.add(state);
            link.parentElement.classList.add(state);
            link.setAttribute("aria-current", isCurrent ? "page" : "location");
        }
        if (address === topUrl) top = item;
    }
    if (top) {
        for (const link of links) {
            const item = link.closest(".site-nav-item");
            // Membership follows the nearest top navigation page, which can differ from tree nesting.
            const owner = item.dataset.topOwner;
            item.classList.toggle("is-in-current-top-section", !!owner && url(prefix + owner) === topUrl);
            item.classList.toggle("is-top-navigation-ancestor", item !== top && item.contains(top));
            item.style.setProperty("--wide-indent-depth", Math.max(0, Number(item.dataset.depth) - Number(top.dataset.depth) - 1));
        }
    }
    // Leave the static sitemap link intact if this script cannot load or initialize.
    list.replaceChildren(content.content);
})();
