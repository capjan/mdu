(() => {
    const content = document.querySelector(".markdown-content");
    if (!content) return;

    const isVector = (source) => /^data:image\/svg|\.svg(?:[?#]|$)/i.test(source);

    // A pixel image already shown at full size would look the same in the viewer.
    const canGrow = (image) => {
        if (isVector(image.currentSrc || image.src)) return true;
        const shown = image.getBoundingClientRect();
        return image.naturalWidth > shown.width + 1 || image.naturalHeight > shown.height + 1;
    };

    // `images.zoom: false` in the front matter; the closest class on an image or around it wins.
    const pageZoom = document.currentScript?.dataset.imageZoom !== "off";
    const allowsZoom = (image) => {
        if (image.getClientRects().length === 0) return false;
        const setting = image.closest(".image-zoom-on, .image-zoom-off");
        return setting ? setting.matches(".image-zoom-on") : pageZoom && canGrow(image);
    };

    const icon = (path) => `<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${path}"/></svg>`;

    const createLightbox = () => {
        const dialog = document.createElement("dialog");
        dialog.className = "image-lightbox";
        dialog.innerHTML = `<div class="image-lightbox-stage" tabindex="-1" autofocus><img alt="" draggable="false"></div>
<p class="image-lightbox-caption"></p>
<button class="image-lightbox-close" type="button" aria-label="Close image" title="Close (Esc)">${icon("M5 5l10 10M15 5L5 15")}</button>
<div class="image-lightbox-tools" role="group" aria-label="Image zoom">
<button type="button" data-zoom="out" aria-label="Zoom out" title="Zoom out (-)">${icon("M4.5 10h11")}</button>
<span class="image-lightbox-level"></span>
<button type="button" data-zoom="in" aria-label="Zoom in" title="Zoom in (+)">${icon("M4.5 10h11M10 4.5v11")}</button>
<button type="button" data-zoom="fit" aria-label="Fit image to window" title="Fit to window (0)">${icon("M3.5 7.5v-4h4M16.5 7.5v-4h-4M3.5 12.5v4h4M16.5 12.5v4h-4")}</button>
<button type="button" data-zoom="actual" aria-label="Actual size" title="Actual size (1)">1:1</button>
</div>
<div class="image-lightbox-gallery" role="group" aria-label="Image gallery" hidden>
<button type="button" aria-label="Previous image" title="Previous image">${icon("M12 5l-5 5 5 5")}</button>
<span class="image-lightbox-level" aria-live="polite" aria-atomic="true"></span>
<button type="button" aria-label="Next image" title="Next image">${icon("M8 5l5 5-5 5")}</button>
</div>`;
        document.body.append(dialog);

        const stage = dialog.querySelector(".image-lightbox-stage");
        const preview = stage.querySelector("img");
        const caption = dialog.querySelector(".image-lightbox-caption");
        const closeButton = dialog.querySelector(".image-lightbox-close");
        const tools = dialog.querySelector(".image-lightbox-tools");
        const level = dialog.querySelector(".image-lightbox-level");
        const zoomButtons = Object.fromEntries([...tools.querySelectorAll("button")].map((button) => [button.dataset.zoom, button]));

        const navigation = dialog.querySelector(".image-lightbox-gallery");
        const position = navigation.querySelector("span");
        const [previous, next] = navigation.querySelectorAll("button");
        let gallery = null;
        let activeImage = null;
        let activeIndex = 0;

        // The view: image size, zoom limits, and the image's top-left corner in stage pixels.
        const view = { width: 0, height: 0, scale: 1, fit: 1, max: 1, x: 0, y: 0, zoomable: false, fitted: true, vector: false };
        let sourceSize = { width: 0, height: 0 };
        let frame = null;
        let animateNextPaint = false;

        const area = () => {
            const width = stage.clientWidth;
            const height = stage.clientHeight;
            const side = width < 760 ? 8 : 32;
            return { left: side, top: 64, width: Math.max(1, width - side * 2), height: Math.max(1, height - (navigation.hidden ? 128 : 176)) };
        };
        const place = (position, size, start, length) => size <= length
            ? start + (length - size) / 2
            : Math.min(start, Math.max(start + length - size, position));

        const paint = () => {
            if (frame !== null) cancelAnimationFrame(frame);
            frame = null;
            const box = area();
            const width = view.width * view.scale;
            const height = view.height * view.scale;
            stage.classList.toggle("is-animating", animateNextPaint);
            stage.classList.toggle("is-zoomable", view.zoomable && view.fitted);
            stage.classList.toggle("is-pannable", width > box.width + 0.5 || height > box.height + 0.5);
            stage.classList.toggle("is-zoomed", view.zoomable && !view.fitted);
            preview.style.width = `${width}px`;
            preview.style.height = `${height}px`;
            preview.style.transform = `translate(${view.x}px, ${view.y}px)`;
            level.textContent = `${Math.round(view.scale * 100)}%`;
            zoomButtons.out.setAttribute("aria-disabled", String(view.fitted));
            zoomButtons.in.setAttribute("aria-disabled", String(view.scale >= view.max * 0.999));
            zoomButtons.fit.setAttribute("aria-disabled", String(view.fitted));
            zoomButtons.actual.setAttribute("aria-disabled", String(Math.abs(view.scale - 1) < 0.001 || view.fit > 1));
        };
        // Keeps the view valid right away and writes to the page once per frame.
        const update = (animate = false) => {
            const box = area();
            view.x = place(view.x, view.width * view.scale, box.left, box.width);
            view.y = place(view.y, view.height * view.scale, box.top, box.height);
            view.fitted = view.scale <= view.fit * 1.001;
            animateNextPaint = animate;
            if (frame === null) frame = requestAnimationFrame(paint);
        };
        const zoomTo = (scale, centerX, centerY, animate = false) => {
            const next = Math.min(view.max, Math.max(view.fit, scale));
            const box = area();
            const anchorX = centerX ?? box.left + box.width / 2;
            const anchorY = centerY ?? box.top + box.height / 2;
            view.x = anchorX - (anchorX - view.x) * next / view.scale;
            view.y = anchorY - (anchorY - view.y) * next / view.scale;
            view.scale = next;
            update(animate);
        };
        const measure = (keepView) => {
            const width = preview.naturalWidth || sourceSize.width;
            const height = preview.naturalHeight || sourceSize.height;
            if (!width || !height) return;
            const box = area();
            const ratio = Math.min(box.width / width, box.height / height);
            const wasFitted = view.fitted;
            view.width = width;
            view.height = height;
            view.zoomable = view.vector || ratio < 1;
            // Vector images stay sharp, so they may start enlarged, but a small icon must not fill the window. Pixel images never start enlarged.
            view.fit = view.vector ? Math.min(3, ratio) : Math.min(1, ratio);
            view.max = view.vector ? Math.max(4, view.fit * 6) : view.zoomable ? 4 : view.fit;
            tools.hidden = !view.zoomable;
            view.scale = keepView && !wasFitted ? Math.min(view.max, Math.max(view.fit, view.scale)) : view.fit;
            update();
            paint();
        };
        const reveal = () => {
            measure(false);
            stage.classList.remove("is-loading");
        };
        preview.addEventListener("load", reveal);
        preview.addEventListener("error", () => dialog.close());
        const updateCaptionFocus = () => {
            const scrollable = caption.scrollHeight > caption.clientHeight;
            if (scrollable) caption.tabIndex = 0;
            else caption.removeAttribute("tabindex");
        };
        new ResizeObserver(updateCaptionFocus).observe(caption);
        new ResizeObserver(() => {
            if (dialog.open) measure(true);
        }).observe(stage);

        const zoomStep = (direction) => zoomTo(view.scale * (direction > 0 ? 1.5 : 1 / 1.5), undefined, undefined, true);
        const zoomActions = {
            in: () => zoomStep(1),
            out: () => zoomStep(-1),
            fit: () => zoomTo(view.fit, undefined, undefined, true),
            // An image that starts enlarged has no zoom level for its actual size.
            actual: () => {
                if (view.fit <= 1) zoomTo(1, undefined, undefined, true);
            },
        };
        tools.addEventListener("click", (event) => {
            const button = event.target.closest("button");
            if (button && button.getAttribute("aria-disabled") !== "true") zoomActions[button.dataset.zoom]();
        });
        closeButton.addEventListener("click", () => dialog.close());

        dialog.addEventListener("keydown", (event) => {
            if (event.target === caption) return;
            // A held Enter must not close the viewer it just opened, and then reopen it.
            if (event.repeat && (event.key === "Enter" || event.key === " ")) {
                event.preventDefault();
                return;
            }
            if (event.metaKey || event.ctrlKey || event.altKey) return;
            if (!navigation.hidden && view.fitted && (event.key === "ArrowLeft" || event.key === "ArrowRight")) {
                move(event.key === "ArrowLeft" ? -1 : 1);
                event.preventDefault();
                return;
            }
            if (!view.zoomable) return;
            const pan = { ArrowLeft: [1, 0], ArrowRight: [-1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] }[event.key];
            if (pan) {
                view.x += pan[0] * 80;
                view.y += pan[1] * 80;
                update(true);
            } else if (event.key === "+" || event.key === "=") zoomActions.in();
            else if (event.key === "-" || event.key === "_") zoomActions.out();
            else if (event.key === "0") zoomActions.fit();
            else if (event.key === "1") zoomActions.actual();
            else return;
            event.preventDefault();
        });

        // A smooth mouse wheel and a trackpad send the same events, so the direction decides: sideways pans, vertical zooms.
        dialog.addEventListener("wheel", (event) => {
            if (event.target === caption && caption.scrollHeight > caption.clientHeight) return;
            event.preventDefault();
            if (!view.zoomable) return;
            const unit = event.deltaMode === WheelEvent.DOM_DELTA_LINE ? 16 : 1;
            if (!event.ctrlKey && Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
                view.x -= event.deltaX * unit;
                view.y -= event.deltaY * unit;
                update();
                return;
            }
            const delta = event.deltaY * unit;
            // Trackpad pinches arrive as wheel events with ctrlKey and small deltas. Ctrl with a mouse wheel looks the same
            // but has large deltas, so the limit keeps one wheel step at about the size of a step without ctrl.
            const amount = event.ctrlKey ? Math.max(-25, Math.min(25, delta)) * 0.01 : delta * 0.002;
            zoomTo(view.scale * Math.exp(-amount), event.clientX, event.clientY);
        }, { passive: false });

        const pointers = new Map();
        let press = null;
        let pinch = null;
        let lastToggle = 0;

        // Safari reports trackpad pinches as gesture events instead of wheel events.
        let gestureStart = 1;
        dialog.addEventListener("gesturestart", (event) => {
            event.preventDefault();
            gestureStart = view.scale;
        });
        dialog.addEventListener("gesturechange", (event) => {
            event.preventDefault();
            if (view.zoomable && pointers.size < 2) zoomTo(gestureStart * event.scale, event.clientX, event.clientY);
        });

        const pinchState = () => {
            const [first, second] = [...pointers.values()];
            return {
                distance: Math.hypot(first.x - second.x, first.y - second.y),
                x: (first.x + second.x) / 2,
                y: (first.y + second.y) / 2,
            };
        };
        // A release can get lost, for example to a context menu or when the dialog closes during a drag.
        const forgetPointers = () => {
            pointers.clear();
            press = null;
            pinch = null;
            stage.classList.remove("is-panning");
        };
        dialog.addEventListener("close", forgetPointers);
        stage.addEventListener("contextmenu", forgetPointers);
        stage.addEventListener("pointerdown", (event) => {
            // On a Mac, ctrl+click opens the context menu.
            if (event.button !== 0 || event.ctrlKey) return;
            stage.setPointerCapture(event.pointerId);
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (pointers.size === 1) {
                press = { x: event.clientX, y: event.clientY, viewX: view.x, viewY: view.y, moved: false };
            } else if (pointers.size === 2) {
                press = null;
                pinch = pinchState();
            }
        });
        stage.addEventListener("pointermove", (event) => {
            if (!pointers.has(event.pointerId)) return;
            if (event.pointerType === "mouse" && !(event.buttons & 1)) {
                forgetPointers();
                return;
            }
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (pinch && pointers.size === 2) {
                const current = pinchState();
                view.x += current.x - pinch.x;
                view.y += current.y - pinch.y;
                if (view.zoomable) zoomTo(view.scale * current.distance / pinch.distance, current.x, current.y);
                pinch = current;
            } else if (press) {
                const deltaX = event.clientX - press.x;
                const deltaY = event.clientY - press.y;
                if (!press.moved && Math.hypot(deltaX, deltaY) < 4) return;
                press.moved = true;
                stage.classList.add("is-panning");
                view.x = press.viewX + deltaX;
                view.y = press.viewY + deltaY;
                update();
            }
        });
        const release = (event) => {
            if (!pointers.delete(event.pointerId)) return;
            stage.classList.remove("is-panning");
            if (pointers.size < 2) pinch = null;
            const tap = press && !press.moved && event.type === "pointerup";
            if (pointers.size === 0) press = null;
            if (!tap) return;
            // From the view, not the page: the image may still be animating to this position.
            const onImage = event.clientX >= view.x && event.clientX <= view.x + view.width * view.scale
                && event.clientY >= view.y && event.clientY <= view.y + view.height * view.scale;
            if (!onImage) {
                dialog.close();
                return;
            }
            // The second click of a double click must not undo the first one.
            if (!view.zoomable || event.timeStamp - lastToggle < 400) return;
            lastToggle = event.timeStamp;
            if (view.fitted) zoomTo(Math.max(1, view.fit * 2), event.clientX, event.clientY, true);
            else zoomTo(view.fit, event.clientX, event.clientY, true);
        };
        stage.addEventListener("pointerup", release);
        stage.addEventListener("pointercancel", release);

        const galleryImages = () => [...gallery.querySelectorAll("img")]
            .filter((image) => image.closest(".image-gallery") === gallery && viewerImage(image));

        const updateNavigation = (images) => {
            navigation.hidden = images.length < 2;
            if (navigation.hidden && navigation.contains(document.activeElement)) closeButton.focus();
            const label = navigation.hidden ? "" : `${activeIndex + 1} / ${images.length}`;
            if (position.textContent !== label) position.textContent = label;
            previous.setAttribute("aria-disabled", String(activeIndex === 0));
            next.setAttribute("aria-disabled", String(activeIndex >= images.length - 1));
        };

        const showImage = (image, openedByKeyboard) => {
            activeImage = image;
            const source = image.currentSrc || image.src;
            const bounds = image.getBoundingClientRect();
            sourceSize = { width: bounds.width, height: bounds.height };
            view.vector = isVector(source);
            view.fitted = true;
            stage.classList.add("is-loading");
            preview.alt = image.alt;
            preview.classList.toggle("image-frame", image.classList.contains("image-frame"));
            const figureCaption = image.closest("figure")?.querySelector(":scope > figcaption")?.textContent.trim();
            const label = figureCaption || image.alt;
            caption.classList.toggle("has-figure-caption", Boolean(figureCaption));
            caption.textContent = label;
            caption.hidden = !label;
            dialog.setAttribute("aria-label", label || "Image");
            if (!dialog.open) {
                dialog.showModal();
                // Keyboard users start on a control with a visible focus ring; pointer users see no ring.
                if (openedByKeyboard) closeButton.focus();
            }
            caption.scrollTop = 0;
            updateCaptionFocus();
            preview.src = source;
            if (preview.complete && preview.naturalWidth) reveal();
            else if (gallery) {
                // Reset zoom immediately while the new preview is loading.
                view.scale = view.fit;
                update();
            }
        };

        const move = (step) => {
            const images = galleryImages();
            const current = images.indexOf(activeImage);
            const index = (current < 0 ? activeIndex : current) + step;
            if (!images[index]) return;
            activeIndex = index;
            updateNavigation(images);
            forgetPointers();
            lastToggle = 0;
            showImage(images[index]);
        };
        previous.addEventListener("click", () => move(-1));
        next.addEventListener("click", () => move(1));

        dialog.addEventListener("close", () => {
            if (dialog.open || !gallery) return;
            const images = galleryImages();
            const image = images.includes(activeImage) ? activeImage : images[activeIndex];
            if (!image) return;
            const target = image.closest("a") ?? image;
            if (target.tabIndex < 0 && !target.hasAttribute("tabindex")) {
                target.tabIndex = -1;
                target.focus();
                target.removeAttribute("tabindex");
            } else target.focus();
        });

        return {
            show(image, openedByKeyboard) {
                gallery = image.closest(".image-gallery");
                const images = gallery ? galleryImages() : [];
                activeIndex = images.indexOf(image);
                updateNavigation(images);
                showImage(image, openedByKeyboard);
            },
            refreshGallery() {
                if (!dialog.open || !gallery) return;
                const images = galleryImages();
                const current = images.indexOf(activeImage);
                activeIndex = current < 0 ? Math.min(activeIndex, images.length - 1) : current;
                updateNavigation(images);
                if (current < 0) {
                    if (images[activeIndex]) showImage(images[activeIndex]);
                    else dialog.close();
                } else measure(true);
            },
        };
    };

    // Returns the image that an event on this element opens, or null when the element keeps its own behavior.
    const viewerImage = (target) => {
        // Enter on a link targets the link, not the image inside it.
        const image = target.closest("img") ?? target.closest("a")?.querySelector("img");
        if (!image || !allowsZoom(image)) return null;
        const link = image.closest("a");
        return link && link.href !== image.src && link.href !== image.currentSrc ? null : image;
    };

    let lightbox;
    const open = (image, openedByKeyboard) => {
        lightbox ??= createLightbox();
        lightbox.show(image, openedByKeyboard);
    };
    content.addEventListener("click", (event) => {
        const image = viewerImage(event.target);
        if (!image) return;
        // Modifier keys ask the browser to open a link in a new tab or window.
        if (image.closest("a") && (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)) return;
        event.preventDefault();
        // A click from the keyboard has no click count.
        open(image, event.detail === 0);
    });

    // The cursor and the tab stop follow what a click would do. Images in links already take keyboard focus, and
    // decorative images would only add stops; the mouse can still open them.
    const showOpens = (image, opens) => {
        image.classList.toggle("image-zoomable", opens);
        const isStop = opens && image.alt && !image.closest("a, button, label, summary, .footnote-preview");
        if (isStop === image.classList.contains("image-lightbox-trigger")) return;
        image.classList.toggle("image-lightbox-trigger", isStop);
        if (isStop) {
            image.tabIndex = 0;
            image.setAttribute("role", "button");
            image.setAttribute("aria-haspopup", "dialog");
        } else {
            image.removeAttribute("tabindex");
            image.removeAttribute("role");
            image.removeAttribute("aria-haspopup");
        }
    };
    const refresh = (images) => {
        const states = images.map((image) => [image, Boolean(viewerImage(image))]);
        for (const [image, opens] of states) showOpens(image, opens);
    };
    new MutationObserver(() => {
        refresh([...content.querySelectorAll("img")]);
        lightbox?.refreshGallery();
    })
        .observe(document.documentElement, { attributes: true, attributeFilter: ["data-color-scheme"] });
    // A change of the shown size, such as in a narrower window or an opened section, can change the answer.
    const sizes = new ResizeObserver((entries) => refresh(entries.map((entry) => entry.target)));
    for (const image of content.querySelectorAll("img")) {
        sizes.observe(image);
        if (!image.complete) image.addEventListener("load", () => refresh([image]), { once: true });
    }
    content.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        const image = viewerImage(event.target);
        if (!image) return;
        const isTrigger = event.target.matches(".image-lightbox-trigger");
        if (!isTrigger && event.key === " ") return;
        // A held key must not reopen the viewer after it closed and returned focus here.
        if (event.repeat) {
            event.preventDefault();
        } else if (isTrigger) {
            event.preventDefault();
            open(image, true);
        }
    });
})();
