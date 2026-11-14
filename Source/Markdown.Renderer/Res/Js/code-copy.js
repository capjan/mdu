(() => {
    const resetTimers = new WeakMap();

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
            let copied = false;
            try {
                copied = document.execCommand("copy");
            } catch { }
            field.remove();
            return copied;
        }
    };

    // Code annotations. A click on a marker opens its explanation as a popover without this script; the script adds
    // what makes them feel like footnote previews:
    // - pointing at a marker opens its explanation after a short delay, and leaving marker and explanation closes it,
    //   so the pointer can move onto the explanation to select text or follow a link;
    // - a click pins the explanation until a click elsewhere or Esc, and a click on a pinned marker closes it;
    // - keyboard focus opens the explanation, as for footnotes;
    // - the explanation opens next to the marker that was used, also when its number appears more than once;
    // - the marker of an open explanation and its legend entry are highlighted, and so is a marker whose legend entry
    //   is pointed at;
    // - the legend is open while the page prints.
    const annotationOpenDelay = 100;
    const annotationCloseDelay = 125;
    const openingMarkers = new WeakMap();
    const annotationTimers = new WeakMap();
    let pinnedAnnotation = null;

    const annotationPopover = (marker) => document.getElementById(marker.getAttribute("popovertarget"));
    const annotationMarkers = (popover) =>
        document.querySelectorAll(`.code-annotation[popovertarget="${CSS.escape(popover.id)}"]`);
    const legendEntries = (popover) =>
        document.querySelectorAll(`.code-legend li[data-annotation="${CSS.escape(popover.id)}"]`);
    const isPointedAt = (popover) =>
        popover.matches(":hover") || [...annotationMarkers(popover)].some((marker) => marker.matches(":hover"));

    const placeAnnotation = (marker, popover) => {
        openingMarkers.set(popover, marker);
        const anchor = marker.style.getPropertyValue("anchor-name");
        if (anchor) popover.style.setProperty("position-anchor", anchor);
    };
    const clearAnnotationTimer = (popover) => window.clearTimeout(annotationTimers.get(popover));
    const openAnnotation = (marker) => {
        const popover = annotationPopover(marker);
        if (!popover) return;
        clearAnnotationTimer(popover);
        if (popover.matches(":popover-open") && openingMarkers.get(popover) === marker) return;
        if (popover.matches(":popover-open")) popover.hidePopover();
        placeAnnotation(marker, popover);
        popover.showPopover();
    };
    const closeAnnotationLater = (popover) => {
        clearAnnotationTimer(popover);
        annotationTimers.set(popover, window.setTimeout(() => {
            if (popover === pinnedAnnotation || isPointedAt(popover) || popover.contains(document.activeElement)) return;
            if ([...annotationMarkers(popover)].some((marker) => marker.matches(":focus-visible"))) return;
            if (popover.matches(":popover-open")) popover.hidePopover();
        }, annotationCloseDelay));
    };

    document.addEventListener("pointerover", (event) => {
        if (event.pointerType !== "mouse") return;
        const marker = event.target.closest?.(".code-annotation");
        if (marker && !marker.contains(event.relatedTarget)) {
            const popover = annotationPopover(marker);
            if (!popover) return;
            clearAnnotationTimer(popover);
            annotationTimers.set(popover, window.setTimeout(() => openAnnotation(marker), annotationOpenDelay));
            return;
        }
        const popover = event.target.closest?.(".code-annotations > [popover]");
        if (popover) clearAnnotationTimer(popover);
    });

    document.addEventListener("pointerout", (event) => {
        if (event.pointerType !== "mouse") return;
        const owner = event.target.closest?.(".code-annotation, .code-annotations > [popover]");
        if (!owner || owner.contains(event.relatedTarget)) return;
        const popover = owner.matches(".code-annotation") ? annotationPopover(owner) : owner;
        if (popover) closeAnnotationLater(popover);
    });

    document.addEventListener("focusin", (event) => {
        const marker = event.target.closest?.(".code-annotation");
        if (marker?.matches(":focus-visible")) openAnnotation(marker);
    });

    document.addEventListener("focusout", (event) => {
        const marker = event.target.closest?.(".code-annotation");
        const popover = marker && annotationPopover(marker);
        if (popover) closeAnnotationLater(popover);
    });

    // A click pins an explanation that pointing or focus opened, instead of letting the button toggle it closed. The
    // click runs before the browser toggles the popover, so placing it here also positions it right away.
    document.addEventListener("click", (event) => {
        const marker = event.target.closest(".code-annotation");
        const popover = marker && annotationPopover(marker);
        if (!popover) return;
        clearAnnotationTimer(popover);
        const open = popover.matches(":popover-open");
        if (open && pinnedAnnotation === popover && openingMarkers.get(popover) === marker) {
            pinnedAnnotation = null;
            return;
        }
        pinnedAnnotation = popover;
        if (open) {
            event.preventDefault();
            if (openingMarkers.get(popover) !== marker) openAnnotation(marker);
            return;
        }
        placeAnnotation(marker, popover);
    });

    // The marker that opened an explanation and the explanation's legend entry stay highlighted while it is open.
    // Toggle events do not bubble, so the document listens while they pass down.
    document.addEventListener("toggle", (event) => {
        const popover = event.target;
        if (!(popover instanceof HTMLElement) || !popover.matches(".code-annotations > [popover]")) return;
        const open = event.newState === "open";
        if (!open && pinnedAnnotation === popover) pinnedAnnotation = null;
        annotationMarkers(popover).forEach((marker) => marker.classList.remove("is-open"));
        if (open) (openingMarkers.get(popover) ?? annotationMarkers(popover)[0])?.classList.add("is-open");
        legendEntries(popover).forEach((entry) => entry.classList.toggle("is-active", open));
    }, true);

    // Pointing at a legend entry highlights its markers in the code.
    const highlightMarkers = (entry, on) => {
        const id = entry?.dataset.annotation;
        if (!id) return;
        document.querySelectorAll(`.code-annotation[popovertarget="${CSS.escape(id)}"]`)
            .forEach((marker) => marker.classList.toggle("is-highlighted", on));
    };
    document.addEventListener("pointerover", (event) => {
        const entry = event.target.closest?.(".code-legend li[data-annotation]");
        if (entry && !entry.contains(event.relatedTarget)) highlightMarkers(entry, true);
    });
    document.addEventListener("pointerout", (event) => {
        const entry = event.target.closest?.(".code-legend li[data-annotation]");
        if (entry && !entry.contains(event.relatedTarget)) highlightMarkers(entry, false);
    });

    // Print shows every explanation: open the legends for the printout and restore them afterwards.
    let legendsOpenedForPrint = [];
    window.addEventListener("beforeprint", () => {
        legendsOpenedForPrint = [...document.querySelectorAll("details.code-legend:not([open])")];
        legendsOpenedForPrint.forEach((legend) => { legend.open = true; });
    });
    window.addEventListener("afterprint", () => {
        legendsOpenedForPrint.forEach((legend) => { legend.open = false; });
        legendsOpenedForPrint = [];
    });

    document.addEventListener("click", async (event) => {
        const button = event.target.closest(".code-block-copy");
        if (!button) return;

        const body = button.closest(".code-block-body");
        const code = body?.querySelector("pre code");
        if (!code) return;

        // Annotated code carries its text without the markers.
        const copied = await copyText(body.dataset.copy ?? code.textContent ?? "");
        const label = button.querySelector("span");
        clearTimeout(resetTimers.get(button));
        label.textContent = copied ? "Copied" : "Copy failed";
        button.setAttribute("aria-label", copied ? "Copied to clipboard" : "Could not copy code");
        button.title = copied ? "Copied to clipboard" : "Could not copy code";
        button.classList.toggle("is-copied", copied);
        button.classList.toggle("is-failed", !copied);
        resetTimers.set(button, setTimeout(() => {
            label.textContent = "Copy";
            button.setAttribute("aria-label", "Copy code");
            button.title = "Copy code";
            button.classList.remove("is-copied");
            button.classList.remove("is-failed");
        }, 1500));
    });
})();
