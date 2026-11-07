(() => {
    const content = document.querySelector(".markdown-content");
    if (!content) return;

    const clearFootnoteHighlights = () => {
        content.querySelectorAll("a.footnote-ref.is-highlighted, .footnotes > ol > li.is-active").forEach((item) => {
            item.classList.remove("is-highlighted", "is-active");
        });
    };

    const resetFloating = (preview) => {
        preview.style.maxWidth = "";
        preview.style.maxHeight = "";
        preview.style.overflow = "";
        preview.style.top = "";
        preview.style.left = "";
    };
    // Places a fixed preview above or below its anchor within the content column.
    // Returns false when the anchor is scrolled out of view.
    const positionFloating = (preview, anchor, { maxWidth, gap, centered = false }) => {
        const referenceBounds = anchor.getBoundingClientRect();
        const contentBounds = content.getBoundingClientRect();
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = document.documentElement.clientHeight;
        if (referenceBounds.bottom <= 0 || referenceBounds.top >= viewportHeight
            || referenceBounds.right <= 0 || referenceBounds.left >= viewportWidth) {
            return false;
        }

        resetFloating(preview);
        const contentLeft = Math.max(gap, contentBounds.left);
        const contentRight = Math.min(viewportWidth - gap, contentBounds.right);
        const availableWidth = Math.max(1, contentRight - contentLeft);
        preview.style.maxWidth = `${Math.min(maxWidth, availableWidth)}px`;

        const previewBounds = preview.getBoundingClientRect();
        const spaceAbove = Math.max(0, referenceBounds.top - gap);
        const spaceBelow = Math.max(0, viewportHeight - referenceBounds.bottom - gap);
        const showAbove = previewBounds.height <= spaceAbove || spaceAbove >= spaceBelow;
        const availableHeight = showAbove ? spaceAbove : spaceBelow;
        if (previewBounds.height > availableHeight) {
            preview.style.maxHeight = `${availableHeight}px`;
            preview.style.overflow = "auto";
        }

        const positionedBounds = preview.getBoundingClientRect();
        const top = showAbove
            ? referenceBounds.top - positionedBounds.height - gap
            : referenceBounds.bottom + gap;
        const maxLeft = contentRight - positionedBounds.width;
        let left = centered
            ? referenceBounds.left + (referenceBounds.width - positionedBounds.width) / 2
            : referenceBounds.left;
        if (!centered && left > maxLeft) left = referenceBounds.right - positionedBounds.width;
        left = Math.max(contentLeft, Math.min(left, maxLeft));
        preview.style.top = `${Math.max(gap, Math.min(top, viewportHeight - positionedBounds.height - gap))}px`;
        preview.style.left = `${left}px`;
        return true;
    };

    // Short hints on elements with data-tooltip share the footnote preview look and placement,
    // and appear at once instead of after the browser's native title delay.
    const tooltip = document.createElement("span");
    tooltip.className = "footnote-preview content-tooltip";
    tooltip.id = "content-tooltip";
    tooltip.setAttribute("role", "tooltip");
    document.body.append(tooltip);
    let tooltipAnchor;
    let tooltipFrame;
    const scheduleTooltipPosition = () => {
        if (tooltipFrame !== undefined) return;
        tooltipFrame = window.requestAnimationFrame(() => {
            tooltipFrame = undefined;
            if (tooltipAnchor && !positionFloating(tooltip, tooltipAnchor, { maxWidth: 280, gap: 8, centered: true })) hideTooltip();
        });
    };
    // A short hint belongs to the pointer position, so scrolling or typing dismisses it.
    const hideTooltip = () => {
        if (!tooltipAnchor) return;
        tooltipAnchor.removeAttribute("aria-describedby");
        tooltipAnchor = undefined;
        if (tooltipFrame !== undefined) {
            window.cancelAnimationFrame(tooltipFrame);
            tooltipFrame = undefined;
        }
        tooltip.classList.remove("is-visible");
        resetFloating(tooltip);
        window.removeEventListener("resize", scheduleTooltipPosition);
        window.removeEventListener("scroll", hideTooltip, true);
    };
    const showTooltip = (anchor) => {
        if (tooltipAnchor === anchor) return;
        hideTooltip();
        tooltipAnchor = anchor;
        tooltip.textContent = anchor.dataset.tooltip;
        anchor.setAttribute("aria-describedby", tooltip.id);
        tooltip.classList.add("is-visible");
        if (!positionFloating(tooltip, anchor, { maxWidth: 280, gap: 8, centered: true })) {
            hideTooltip();
            return;
        }
        window.addEventListener("resize", scheduleTooltipPosition);
        window.addEventListener("scroll", hideTooltip, true);
    };
    const tooltipAnchorOf = (target) => {
        const anchor = target instanceof Element ? target.closest("[data-tooltip]") : null;
        return anchor && content.contains(anchor) ? anchor : null;
    };
    ["pointerover", "focusin"].forEach((type) => content.addEventListener(type, (event) => {
        const anchor = tooltipAnchorOf(event.target);
        if (anchor) showTooltip(anchor);
    }));
    ["pointerout", "focusout"].forEach((type) => content.addEventListener(type, (event) => {
        const anchor = tooltipAnchorOf(event.target);
        if (anchor && !anchor.contains(event.relatedTarget)) hideTooltip();
    }));
    content.addEventListener("input", hideTooltip);

    const abbreviationTooltip = document.createElement("span");
    abbreviationTooltip.className = "footnote-preview";
    abbreviationTooltip.id = "abbreviation-tooltip";
    abbreviationTooltip.setAttribute("role", "tooltip");
    document.body.append(abbreviationTooltip);
    let abbreviationAnchor;
    let hoveredAbbreviation;
    let focusedAbbreviation;
    let focusedElement;
    let tooltipHovered = false;
    let describedElement;
    let previousDescribedBy;
    let abbreviationHideTimer;
    let abbreviationPositionFrame;
    const cancelAbbreviationHide = () => {
        if (abbreviationHideTimer === undefined) return;
        window.clearTimeout(abbreviationHideTimer);
        abbreviationHideTimer = undefined;
    };
    const scheduleAbbreviationPosition = () => {
        if (abbreviationPositionFrame !== undefined) return;
        abbreviationPositionFrame = window.requestAnimationFrame(() => {
            abbreviationPositionFrame = undefined;
            if (abbreviationAnchor && !positionFloating(abbreviationTooltip, abbreviationAnchor, { maxWidth: 420, gap: 12 }))
                hideAbbreviationTooltip();
        });
    };
    const clearAbbreviationDescription = () => {
        if (!describedElement) return;
        if (previousDescribedBy === null) describedElement.removeAttribute("aria-describedby");
        else describedElement.setAttribute("aria-describedby", previousDescribedBy);
        describedElement = undefined;
        previousDescribedBy = undefined;
    };
    const describeAbbreviationTooltipOn = (element) => {
        if (describedElement === element) return;
        clearAbbreviationDescription();
        if (!element) return;
        previousDescribedBy = element.getAttribute("aria-describedby");
        const describedBy = new Set((previousDescribedBy || "").split(/\s+/).filter(Boolean));
        describedBy.add(abbreviationTooltip.id);
        element.setAttribute("aria-describedby", [...describedBy].join(" "));
        describedElement = element;
    };
    const hideAbbreviationTooltip = () => {
        cancelAbbreviationHide();
        if (abbreviationPositionFrame !== undefined) {
            window.cancelAnimationFrame(abbreviationPositionFrame);
            abbreviationPositionFrame = undefined;
        }
        clearAbbreviationDescription();
        abbreviationAnchor = undefined;
        hoveredAbbreviation = undefined;
        focusedAbbreviation = undefined;
        focusedElement = undefined;
        tooltipHovered = false;
        abbreviationTooltip.classList.remove("is-visible");
        resetFloating(abbreviationTooltip);
        window.removeEventListener("resize", scheduleAbbreviationPosition);
        window.removeEventListener("scroll", scheduleAbbreviationPosition, true);
    };
    const showAbbreviationTooltip = (anchor) => {
        if (abbreviationAnchor !== anchor) {
            clearAbbreviationDescription();
            abbreviationAnchor = anchor;
            abbreviationTooltip.textContent = anchor.dataset.abbreviationTooltip;
        }
        cancelAbbreviationHide();
        describeAbbreviationTooltipOn(focusedAbbreviation === anchor ? focusedElement : anchor);
        abbreviationTooltip.classList.add("is-visible");
        if (!positionFloating(abbreviationTooltip, anchor, { maxWidth: 420, gap: 12 })) {
            hideAbbreviationTooltip();
            return;
        }
        window.addEventListener("resize", scheduleAbbreviationPosition);
        window.addEventListener("scroll", scheduleAbbreviationPosition, true);
    };
    const scheduleAbbreviationHide = () => {
        cancelAbbreviationHide();
        abbreviationHideTimer = window.setTimeout(() => {
            abbreviationHideTimer = undefined;
            if (!hoveredAbbreviation && !focusedAbbreviation && !tooltipHovered) hideAbbreviationTooltip();
        }, 125);
    };
    const abbreviationAnchorOf = (target) => {
        const anchor = target instanceof Element ? target.closest("abbr[data-abbreviation-tooltip]") : null;
        return anchor && content.contains(anchor) ? anchor : null;
    };
    const abbreviationForFocus = (target) => {
        const abbreviation = abbreviationAnchorOf(target);
        if (abbreviation) return abbreviation;
        if (!(target instanceof Element) || !target.matches("a[href],button,input,select,textarea,[tabindex]")) return null;
        return target.querySelector("abbr[data-abbreviation-tooltip]");
    };
    content.querySelectorAll("abbr[title]").forEach((abbreviation) => {
        abbreviation.dataset.abbreviationTooltip = abbreviation.getAttribute("title");
        abbreviation.removeAttribute("title");
        if (!abbreviation.hasAttribute("tabindex") &&
            !abbreviation.closest("a[href],button,input,select,textarea,[tabindex]")) abbreviation.tabIndex = 0;
    });
    content.addEventListener("pointerover", (event) => {
        const anchor = abbreviationAnchorOf(event.target);
        if (!anchor) return;
        hoveredAbbreviation = anchor;
        showAbbreviationTooltip(anchor);
    });
    content.addEventListener("pointerout", (event) => {
        const anchor = abbreviationAnchorOf(event.target);
        if (anchor !== hoveredAbbreviation || anchor.contains(event.relatedTarget)) return;
        hoveredAbbreviation = undefined;
        updateAbbreviationTooltip();
    });
    content.addEventListener("focusin", (event) => {
        const anchor = abbreviationForFocus(event.target);
        if (!anchor) return;
        focusedAbbreviation = anchor;
        focusedElement = event.target;
        showAbbreviationTooltip(anchor);
    });
    content.addEventListener("focusout", (event) => {
        if (focusedElement !== event.target || event.target.contains(event.relatedTarget)) return;
        focusedAbbreviation = undefined;
        focusedElement = undefined;
        updateAbbreviationTooltip();
    });
    abbreviationTooltip.addEventListener("pointerenter", () => {
        tooltipHovered = true;
        cancelAbbreviationHide();
    });
    abbreviationTooltip.addEventListener("pointerleave", () => {
        tooltipHovered = false;
        updateAbbreviationTooltip();
    });
    function updateAbbreviationTooltip() {
        const anchor = hoveredAbbreviation || focusedAbbreviation;
        if (anchor) showAbbreviationTooltip(anchor);
        else if (!tooltipHovered) scheduleAbbreviationHide();
    }

    const footnotePreviewClosers = [hideTooltip, hideAbbreviationTooltip];
    const referencesByFootnote = new Map();
    const footnoteReferences = [...content.querySelectorAll("a.footnote-ref")];
    footnoteReferences.forEach((link) => {
        const target = document.getElementById(decodeURIComponent(link.hash.slice(1)));
        if (!target) return;
        const references = referencesByFootnote.get(target) ?? [];
        references.push(link);
        referencesByFootnote.set(target, references);

        const reference = document.createElement("span");
        reference.className = "footnote-reference";
        link.before(reference);
        reference.append(link);
        const preview = document.createElement("span");
        preview.className = "footnote-preview";
        const contentCopy = target.cloneNode(true);
        contentCopy.querySelectorAll("[id]").forEach((element) => element.removeAttribute("id"));
        contentCopy.querySelectorAll(".footnote-back-ref").forEach((backlink) => backlink.remove());
        preview.innerHTML = contentCopy.innerHTML;
        preview.setAttribute("role", "tooltip");
        reference.append(preview);
        let isHovered = false;
        let isFocused = false;
        let hideTimer;
        let positionFrame;
        const cancelHide = () => {
            if (hideTimer !== undefined) {
                window.clearTimeout(hideTimer);
                hideTimer = undefined;
            }
        };
        const positionPreview = () => {
            if (!positionFloating(preview, reference, { maxWidth: 420, gap: 12 })) hide();
        };
        const schedulePositionPreview = () => {
            if (positionFrame !== undefined) return;
            positionFrame = window.requestAnimationFrame(() => {
                positionFrame = undefined;
                if (preview.classList.contains("is-visible")) positionPreview();
            });
        };
        const show = () => {
            cancelHide();
            preview.classList.add("is-visible");
            positionPreview();
            if (!preview.classList.contains("is-visible")) return;
            window.addEventListener("resize", schedulePositionPreview);
            window.addEventListener("scroll", schedulePositionPreview, true);
        };
        const hide = () => {
            cancelHide();
            if (positionFrame !== undefined) {
                window.cancelAnimationFrame(positionFrame);
                positionFrame = undefined;
            }
            preview.classList.remove("is-visible");
            window.removeEventListener("resize", schedulePositionPreview);
            window.removeEventListener("scroll", schedulePositionPreview, true);
            resetFloating(preview);
        };
        const scheduleHide = () => {
            cancelHide();
            hideTimer = window.setTimeout(() => {
                hideTimer = undefined;
                if (!isHovered && !isFocused) hide();
            }, 125);
        };
        reference.addEventListener("pointerenter", () => {
            isHovered = true;
            show();
        });
        reference.addEventListener("pointerleave", () => {
            isHovered = false;
            scheduleHide();
        });
        reference.addEventListener("focusin", () => {
            isFocused = true;
            show();
        });
        reference.addEventListener("focusout", (event) => {
            isFocused = reference.contains(event.relatedTarget);
            if (!isFocused) scheduleHide();
        });
        link.addEventListener("click", () => {
            clearFootnoteHighlights();
            target.classList.add("is-active");
            hide();
        });
        footnotePreviewClosers.push(hide);
    });

    content.querySelectorAll("a.footnote-back-ref").forEach((backlink) => {
        backlink.textContent = "\u21A9\uFE0E";
        backlink.addEventListener("click", () => {
            const footnote = backlink.closest("li[id]");
            if (!footnote) return;
            clearFootnoteHighlights();
            (referencesByFootnote.get(footnote) ?? []).forEach((reference) => {
                reference.classList.add("is-highlighted");
            });
        });
    });

    document.addEventListener("click", (event) => {
        if (!event.target.closest("a.footnote-back-ref, a.footnote-ref")) clearFootnoteHighlights();
    });
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            clearFootnoteHighlights();
            footnotePreviewClosers.forEach((close) => close());
        }
    });
})();
