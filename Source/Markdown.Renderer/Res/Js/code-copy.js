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

    // A code annotation opens next to the marker that was used, also when its number appears more than once. The click
    // runs before the browser opens the popover, so the popover is placed right away.
    document.addEventListener("click", (event) => {
        const marker = event.target.closest(".code-annotation");
        const popover = marker && document.getElementById(marker.getAttribute("popovertarget"));
        const anchor = marker?.style.getPropertyValue("anchor-name");
        if (popover && anchor) popover.style.setProperty("position-anchor", anchor);
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
