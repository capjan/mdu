(() => {
    const toc = document.getElementById("on-this-page");
    const backToTop = document.getElementById("back-to-top");
    const brandTitle = document.getElementById("site-brand-title");
    const header = document.getElementById("site-header");
    const topNavigation = document.querySelector(".top-navigation");
    const breadcrumbs = document.querySelector(".breadcrumbs");
    const pageHeading = document.querySelector(".markdown-content h1");
    let hero = null, heroContent = null, heroFadeHeight = 0;
    // Browsers with scroll-driven animations move the cut-off edge of the hero content in CSS.
    const hasHeroTimeline = CSS.supports("animation-timeline: view()");
    const initialHeaderBottom = header?.getBoundingClientRect().bottom ?? 0;
    const initialNavigationBottom = topNavigation?.getBoundingClientRect().bottom ?? initialHeaderBottom;
    let breadcrumbFadeDistance = breadcrumbs
        ? Math.max(1, breadcrumbs.getBoundingClientRect().top - Math.max(initialHeaderBottom, initialNavigationBottom))
        : 24;
    const links = toc ? Array.from(toc.querySelectorAll(".toc-link")) : [];
    const headings = links
        .map((link) => ({ link, heading: document.getElementById(link.hash.slice(1)) }))
        .filter((item) => item.heading);
    let lastScrollY = window.scrollY;
    let scrollDirection = 1;
    let scrollTravel = 0;
    let scrollFrame = 0;
    let heroSwitchTimer;
    let currentActiveHeading = null;
    let activePermalinkTimer;

    const setActiveHeading = (activeHeading, revealPermalink = false) => {
        for (const item of headings) {
            item.link.classList.toggle("is-current", item === activeHeading);
            if (item === activeHeading) item.link.setAttribute("aria-current", "location");
            else item.link.removeAttribute("aria-current");
        }

        if (currentActiveHeading !== activeHeading || revealPermalink) {
            // Loading the page only marks the first active heading; its permalink stays hidden.
            const isInitialHeading = currentActiveHeading === null;
            currentActiveHeading?.heading.classList.remove("is-toc-active");
            currentActiveHeading = activeHeading;
            if (isInitialHeading && !revealPermalink) return;
            currentActiveHeading.heading.classList.add("is-toc-active");
            window.clearTimeout(activePermalinkTimer);
            activePermalinkTimer = window.setTimeout(() => {
                currentActiveHeading?.heading.classList.remove("is-toc-active");
            }, 1200);
        }
    };

    for (const item of headings) {
        item.link.addEventListener("click", () => setActiveHeading(item, true));
    }

    // A link target inside collapsed details, such as an API operation in a closed tag group, is opened first:
    // its own details when the target is their summary, and every details around it.
    const revealTarget = (id) => {
        const target = id ? document.getElementById(id) : null;
        if (!target) return null;
        let details = target.tagName === "SUMMARY" ? target.parentElement : target.parentElement?.closest("details");
        let opened = false;
        for (; details; details = details.parentElement?.closest("details")) {
            if (details.tagName !== "DETAILS" || details.open) continue;
            details.open = true;
            opened = true;
        }
        return opened ? target : null;
    };
    const decodeHash = (hash) => {
        try { return decodeURIComponent(hash.slice(1)); } catch { return hash.slice(1); }
    };
    // Clicks open the details before the browser scrolls to the target. Search results use full URLs, so every
    // link to this page counts, also one whose hash equals the current one and so fires no hashchange.
    document.addEventListener("click", (event) => {
        const link = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (!link?.hash || link.origin !== location.origin || link.pathname !== location.pathname ||
            link.search !== location.search) return;
        revealTarget(decodeHash(link.hash));
    });
    // On load and on history navigation the browser has already tried to scroll to the hidden target.
    const revealLocationHash = () => revealTarget(decodeHash(location.hash))?.scrollIntoView();
    window.addEventListener("hashchange", revealLocationHash);
    revealLocationHash();

    // The header bars turn transparent while a full-width hero lies under them, and the hero content is cut off
    // at their lower edge.
    const updateHeroUnderHeader = () => {
        const fullHero = document.querySelector(".hero-full");
        if (fullHero !== hero) {
            heroContent?.style.removeProperty("--hero-cut");
            hero = fullHero;
            heroContent = hasHeroTimeline ? null : hero?.querySelector(".hero-content");
            // The bars stay transparent only above the scene, not above its fade into the page.
            heroFadeHeight = hero ? parseFloat(getComputedStyle(hero, "::after").height) || 0 : 0;
        }
        // The bars' full height, also while the navigation slides away: hiding it moves the hero, and a test against
        // its current position would switch the state back and forth.
        const topBarBottom = header.offsetHeight + (topNavigation?.offsetHeight ?? 0);
        const overHero = hero !== null && hero.getBoundingClientRect().bottom - heroFadeHeight > topBarBottom;
        if (overHero !== document.documentElement.classList.contains("is-over-hero")) {
            document.documentElement.classList.add("is-hero-switching");
            window.clearTimeout(heroSwitchTimer);
            heroSwitchTimer = window.setTimeout(() => document.documentElement.classList.remove("is-hero-switching"), 350);
        }
        document.documentElement.classList.toggle("is-over-hero", overHero);
        heroContent?.style.setProperty("--hero-cut", `${Math.max(0, topBarBottom - heroContent.getBoundingClientRect().top)}px`);
    };

    const updateOnScroll = () => {
        scrollFrame = 0;
        document.documentElement.classList.toggle("is-scrolled", window.scrollY > 0);
        updateHeroUnderHeader();
        const navigationShift = (topNavigation?.offsetHeight ?? 0) + 16;
        if (window.scrollY !== lastScrollY) {
            const delta = window.scrollY - lastScrollY;
            lastScrollY = window.scrollY;
            // Only scrolling further than the top navigation is tall turns the direction around,
            // so small movements of the hand do not keep toggling the navigation.
            scrollTravel = Math.sign(delta) === Math.sign(scrollTravel) ? scrollTravel + delta : delta;
            if (Math.abs(scrollTravel) > navigationShift) scrollDirection = Math.sign(scrollTravel);
        }
        if (headings.length > 0) {
            const atScrollablePageEnd = document.documentElement.scrollHeight > window.innerHeight + 1 &&
                window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 1;
            const activeHeading = atScrollablePageEnd
                ? headings[headings.length - 1]
                // Headings inside collapsed details have no box and would always count as passed.
                : [...headings].reverse().find((item) =>
                    item.heading.getClientRects().length > 0 && item.heading.getBoundingClientRect().top <= 150) ?? headings[0];
            setActiveHeading(activeHeading);
        }
        if (brandTitle && pageHeading) {
            const bounds = pageHeading.getBoundingClientRect();
            const headingIsVisible = bounds.bottom > 56 && bounds.top < window.innerHeight;
            brandTitle.textContent = headingIsVisible ? brandTitle.dataset.siteTitle : brandTitle.dataset.pageTitle;
        }
        if (backToTop) {
            backToTop.classList.toggle("is-visible", window.scrollY > 200 && scrollDirection < 0);
        }
        if (topNavigation) {
            // Once hidden, the navigation returns near the top only well below the point where it hid,
            // so it does not flicker around that point.
            const hidden = topNavigation.classList.contains("is-hidden-on-scroll");
            const hideAfter = hidden ? 80 - navigationShift : 80;
            // Over a hero the navigation stays, so the edge where the hero content is cut off does not move.
            const overHero = document.documentElement.classList.contains("is-over-hero");
            topNavigation.classList.toggle("is-hidden-on-scroll", !overHero && window.scrollY > hideAfter && scrollDirection > 0);
        }
        if (breadcrumbs) {
            const headerBottom = header.getBoundingClientRect().bottom;
            const navigationBottom = topNavigation?.getBoundingClientRect().bottom ?? headerBottom;
            const breadcrumbBounds = breadcrumbs.getBoundingClientRect();
            const topBarBottom = Math.max(headerBottom, navigationBottom);
            const opacity = Math.max(0, Math.min(1, (breadcrumbBounds.top - topBarBottom) / breadcrumbFadeDistance));
            breadcrumbs.style.opacity = opacity.toString();
            breadcrumbs.style.visibility = opacity > 0 ? "visible" : "hidden";
        }
    };

    const scheduleScrollUpdate = () => {
        if (!scrollFrame) scrollFrame = window.requestAnimationFrame(updateOnScroll);
    };

    window.addEventListener("scroll", scheduleScrollUpdate, { passive: true });
    window.addEventListener("resize", scheduleScrollUpdate);
    window.addEventListener("load", scheduleScrollUpdate);
    document.addEventListener("mdu:hero-layout", () => {
        if (breadcrumbs) breadcrumbFadeDistance = Math.max(1,
            breadcrumbs.getBoundingClientRect().top + window.scrollY - header.offsetHeight - (topNavigation?.offsetHeight ?? 0));
        scheduleScrollUpdate();
    });
    // The first state is set without transitions, so the bars do not fade from solid to transparent on load.
    document.documentElement.classList.add("is-hero-settling");
    updateHeroUnderHeader();
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
        document.documentElement.classList.remove("is-hero-settling");
    }));
    updateOnScroll();
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
        document.documentElement.classList.remove("panels-initializing");
    }));
})();
