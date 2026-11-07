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

    document.addEventListener("click", async (event) => {
        const button = event.target.closest(".code-block-copy");
        if (!button) return;

        const code = button.closest(".code-block")?.querySelector("pre code");
        if (!code) return;

        const copied = await copyText(code.textContent ?? "");
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
