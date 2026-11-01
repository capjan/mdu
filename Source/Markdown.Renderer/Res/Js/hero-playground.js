(() => {
    const exampleShader = `void mainImage(out vec4 color, in vec2 fragCoord) {
    vec2 uv = (fragCoord - 0.5 * iResolution.xy) / iResolution.y;
    vec2 pointer = iMouse.xy / iResolution.xy - 0.5;
    if (iMouse.x > 0.0) uv -= pointer * 0.05;
    float time = iTime * 0.22;
    float ink = 0.0;
    vec3 glow = vec3(0.0);
    for (int i = 0; i < 16; i++) {
        float depth = float(i) + 1.0 - fract(time);
        float scale = 1.8 / (1.0 + depth * 0.65);
        float angle = time * 0.3 + depth * 0.16;
        float c = cos(angle), s = sin(angle);
        vec2 p = mat2(c, -s, s, c) * uv / scale;
        float sector = mod(atan(p.y, p.x) + 0.523599, 1.047198) - 0.523599;
        float distanceToEdge = abs(length(p) * cos(sector) - 0.65) * scale;
        float aa = 1.5 / iResolution.y;
        float edge = 1.0 - smoothstep(0.001, 0.001 + aa, distanceToEdge);
        float halo = 0.002 / (0.008 + distanceToEdge);
        float fade = smoothstep(0.0, 0.8, depth) * (1.0 - smoothstep(13.0, 16.0, depth));
        float strength = (edge * 0.65 + halo * 0.18) * fade;
        vec3 tint = mix(iForeground, iAccent, 0.25 + 0.5 * sin(depth * 0.3) * sin(depth * 0.3));
        glow += tint * strength;
        ink += strength;
    }
    vec3 tunnel = ink > 0.001 ? glow / ink : iForeground;
    color = vec4(mix(iBackground, tunnel, clamp(ink, 0.0, 0.7)), 1.0);
}`;
    const api = window.mduHero;
    document.querySelectorAll(".hero-playground-template").forEach(template => {
        const hero = template.closest(".hero");
        const controller = api.controllers.get(hero);
        const form = template.content.firstElementChild.cloneNode(true);
        const fullPosition = document.createElement("template");
        fullPosition.className = "hero-full-position";
        const initiallyFull = hero.classList.contains("hero-full");
        const contentPosition = initiallyFull
            ? document.querySelector("template.hero-content-position") : document.createElement("template");
        if (initiallyFull) hero.before(fullPosition);
        else {
            contentPosition.className = "hero-content-position";
            hero.before(contentPosition);
            document.querySelector(".top-navigation").after(fullPosition);
        }
        hero.after(form);
        hero.classList.add("hero-playground-preview");
        controller.error.setAttribute("aria-live", "off");
        form.append(controller.error);
        const field = name => form.elements.namedItem(name);
        const status = form.querySelector(".hero-playground-status");
        const editor = form.querySelector(".hero-shader-editor");
        const yamlOptions = form.querySelector(".hero-yaml-options");
        const returnButton = document.createElement("button");
        returnButton.type = "button";
        returnButton.className = "hero-playground-return";
        returnButton.textContent = "Back to controls";
        returnButton.hidden = true;
        hero.append(returnButton);
        let returnFocus = field("preview");
        const layoutChanged = () => hero.dispatchEvent(new CustomEvent("mdu:hero-layout", { bubbles: true }));
        const content = hero.querySelector(".hero-content");
        const paragraph = className => {
            let node = content.querySelector(`.${className}`);
            if (!node) {
                node = document.createElement("p");
                node.className = className;
                if (className === "hero-title") content.prepend(node);
                else content.querySelector(".hero-title").after(node);
                node.hidden = true;
            }
            return node;
        };
        const title = paragraph("hero-title"), text = paragraph("hero-text");
        let shader = hero.querySelector(".hero-shader");
        if (!shader) {
            shader = document.createElement("template");
            shader.className = "hero-shader";
            hero.append(shader);
        }
        const original = {
            dataset: { ...hero.dataset }, title: title.textContent, text: text.textContent,
            height: hero.style.getPropertyValue("--hero-height"),
            width: initiallyFull ? "full" : "content",
            center: hero.classList.contains("hero-align-center"),
            shader: shader.content.textContent, theme: document.documentElement.dataset.theme
        };
        const motion = matchMedia("(prefers-reduced-motion: reduce)");
        let paused = false, shaderTimer = 0, shaderPending = false, layoutMessage = "";
        const updateStatus = () => {
            const previewMessage = shaderPending ? "Updating shader preview..."
                : !controller.error.hidden ? controller.error.textContent
                : motion.matches ? "Reduced motion is enabled. Previews remain still." : "";
            status.textContent = [layoutMessage, previewMessage].filter(Boolean).join(" ");
            field("copy").disabled = shaderPending || !controller.error.hidden;
        };
        const cancelShaderUpdate = () => {
            clearTimeout(shaderTimer);
            shaderTimer = 0;
            shaderPending = false;
        };
        const previewShader = () => {
            shaderTimer = 0;
            shaderPending = false;
            controller.refresh();
            updateStatus();
        };
        const yaml = () => {
            const quote = value => JSON.stringify(value);
            const source = shader.content.textContent;
            const backticks = source.match(/`+/g) || [];
            const fence = "`".repeat(Math.max(3, ...backticks.map(run => run.length + 1)));
            const lines = [fence + "hero"];
            if (title.textContent.trim()) lines.push(`title: ${quote(title.textContent)}`);
            if (text.textContent.trim()) lines.push(`text: ${quote(text.textContent)}`);
            lines.push(`effect: ${hero.dataset.heroScene}`);
            lines.push(`width: ${hero.classList.contains("hero-full") ? "full" : "content"}`);
            lines.push(`height: ${parseInt(hero.style.getPropertyValue("--hero-height"), 10)}`);
            lines.push(`speed: ${hero.dataset.heroSpeed}`);
            if (hero.classList.contains("hero-align-center")) lines.push("align: center");
            if (hero.dataset.heroQuality !== "auto") lines.push(`quality: ${hero.dataset.heroQuality}`);
            if (hero.dataset.heroScene === "beach")
                lines.push(`time: ${hero.dataset.heroTime}`, `waves: ${hero.dataset.heroWaves}`);
            const colors = (hero.dataset.heroScene === "beach" ? ["water", "shallow", "sand"] : ["accent"])
                .filter(name => hero.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`]);
            if (colors.length) {
                lines.push("colors:");
                for (const name of colors)
                    lines.push(`  ${name}: ${quote(hero.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`])}`);
            }
            const actions = hero.querySelectorAll(".hero-button");
            if (actions.length) {
                lines.push("actions:");
                actions.forEach(action => {
                    lines.push(`  - text: ${quote(action.textContent)}`,
                        `    link: ${quote(action.dataset.heroLink)}`,
                        `    style: ${action.classList.contains("hero-button-primary") ? "primary" : "secondary"}`);
                });
            }
            if (hero.dataset.heroScene === "shader") {
                lines.push("shader: |");
                lines.push(...source.trimEnd().split("\n").map(line => `  ${line}`));
            }
            lines.push(fence);
            field("yaml").value = lines.join("\n");
        };
        const updateColors = () => {
            field("accent").value = controller.accentColor();
            if (hero.dataset.heroScene === "beach") {
                for (const [name, hex] of Object.entries(controller.beachColors())) {
                    field(name).value = hex.length === 4
                        ? "#" + hex.slice(1).split("").map(c => c + c).join("") : hex;
                }
            }
        };
        const updateControls = () => {
            for (const name of ["effect", "speed", "quality", "time", "waves"])
                field(name).value = hero.dataset[name === "effect" ? "heroScene" : `hero${name[0].toUpperCase()}${name.slice(1)}`];
            field("title").value = title.textContent;
            field("text").value = text.textContent;
            field("height").value = parseInt(hero.style.getPropertyValue("--hero-height"), 10);
            field("width").value = hero.classList.contains("hero-full") ? "full" : "content";
            field("align").value = hero.classList.contains("hero-align-center") ? "center" : "left";
            field("theme").value = document.documentElement.dataset.theme;
            for (const name of ["speed", "height", "waves"])
                form.querySelector(`.hero-${name}`).textContent = `${field(name).value}${name === "height" ? " px" : "x"}`;
            form.querySelector(".hero-beach-controls").hidden = hero.dataset.heroScene !== "beach";
            form.querySelector(".hero-accent-control").hidden = hero.dataset.heroScene === "beach";
            editor.hidden = hero.dataset.heroScene !== "shader";
            field("shader").value = shader.content.textContent || exampleShader;
            field("pause").textContent = paused ? "Resume" : "Pause";
            field("pause").setAttribute("aria-pressed", String(paused));
            updateColors();
            yaml();
            updateStatus();
        };
        const setWidth = width => {
            const full = width === "full";
            if (full && [...document.querySelectorAll(".hero-full")].some(other => other !== hero)) {
                layoutMessage = "Only one full-width banner is allowed per page. Change the other banner to content width first.";
                field("width").value = hero.classList.contains("hero-full") ? "full" : "content";
                updateStatus();
                return false;
            }
            layoutMessage = "";
            if (full !== hero.classList.contains("hero-full")) {
                const active = form.contains(document.activeElement) ? document.activeElement : null;
                const anchor = active || form;
                const top = anchor.getBoundingClientRect().top;
                const wasScrolled = window.scrollY > 0;
                hero.classList.toggle("hero-full", full);
                hero.classList.toggle("hero-inline", !full);
                (full ? fullPosition : contentPosition).after(hero, form);
                active?.focus({ preventScroll: true });
                controller.configure();
                layoutChanged();
                if (wasScrolled) window.scrollBy({ top: anchor.getBoundingClientRect().top - top, behavior: "instant" });
            }
            return true;
        };
        form.addEventListener("submit", event => event.preventDefault());
        form.addEventListener("toggle", layoutChanged, { capture: true });
        form.addEventListener("focusin", event => {
            if (event.target.matches("input, select, textarea, summary")) returnFocus = event.target;
        });
        field("preview").addEventListener("click", () => {
            returnButton.hidden = false;
            returnButton.focus({ preventScroll: true });
            hero.scrollIntoView({ behavior: motion.matches ? "instant" : "smooth", block: "start" });
        });
        returnButton.addEventListener("click", () => {
            const target = returnFocus.getClientRects().length ? returnFocus : field("preview");
            target.focus({ preventScroll: true });
            target.scrollIntoView({ behavior: motion.matches ? "instant" : "smooth", block: "center" });
            returnButton.hidden = true;
        });
        field("width").addEventListener("change", () => {
            setWidth(field("width").value);
            yaml();
            updateStatus();
        });
        field("effect").addEventListener("change", () => {
            cancelShaderUpdate();
            hero.dataset.heroScene = field("effect").value;
            if (hero.dataset.heroScene === "shader" && !shader.content.textContent.trim())
                shader.content.textContent = exampleShader;
            controller.refresh();
            updateControls();
        });
        for (const [name, node] of [["title", title], ["text", text]]) {
            field(name).addEventListener("input", () => {
                node.textContent = field(name).value;
                node.hidden = !node.textContent.trim();
                yaml();
            });
        }
        for (const name of ["speed", "quality", "time", "waves"]) {
            field(name).addEventListener(name === "speed" || name === "waves" ? "input" : "change", () => {
                hero.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`] = field(name).value;
                controller.configure();
                if (name === "time") updateColors();
                if (name === "speed" || name === "waves")
                    form.querySelector(`.hero-${name}`).textContent = `${field(name).value}x`;
                yaml();
            });
        }
        field("height").addEventListener("input", () => {
            hero.style.setProperty("--hero-height", `${field("height").value}px`);
            form.querySelector(".hero-height").textContent = `${field("height").value} px`;
            controller.configure();
            layoutChanged();
            yaml();
        });
        field("align").addEventListener("change", () => {
            hero.classList.toggle("hero-align-center", field("align").value === "center");
            hero.classList.toggle("hero-align-left", field("align").value === "left");
            yaml();
        });
        for (const name of ["accent", "water", "shallow", "sand"]) {
            field(name).addEventListener("input", () => {
                hero.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`] = field(name).value;
                controller.configure();
                yaml();
            });
        }
        field("colorsReset").addEventListener("click", () => {
            for (const name of ["heroAccent", "heroWater", "heroShallow", "heroSand"]) delete hero.dataset[name];
            controller.configure();
            updateColors();
            yaml();
        });
        field("theme").addEventListener("change", () => {
            document.documentElement.dataset.theme = field("theme").value;
        });
        new MutationObserver(() => {
            field("theme").value = document.documentElement.dataset.theme;
            updateColors();
        }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
        matchMedia("(prefers-color-scheme: dark)").addEventListener("change", updateColors);
        hero.addEventListener("mdu:hero-error", updateStatus);
        hero.addEventListener("mdu:hero-ready", updateStatus);
        field("pause").addEventListener("click", () => {
            paused = !paused;
            controller.setPaused(paused);
            field("pause").textContent = paused ? "Resume" : "Pause";
            field("pause").setAttribute("aria-pressed", String(paused));
        });
        field("shader").addEventListener("input", () => {
            cancelShaderUpdate();
            shader.content.textContent = field("shader").value;
            shaderPending = true;
            yaml();
            updateStatus();
            shaderTimer = setTimeout(previewShader, 300);
        });
        field("reset").addEventListener("click", () => {
            if (!setWidth(original.width)) return;
            cancelShaderUpdate();
            for (const name of Object.keys(hero.dataset)) delete hero.dataset[name];
            Object.assign(hero.dataset, original.dataset);
            for (const [node, value] of [[title, original.title], [text, original.text]]) {
                node.textContent = value;
                node.hidden = !value.trim();
            }
            hero.style.setProperty("--hero-height", original.height);
            hero.classList.toggle("hero-align-center", original.center);
            hero.classList.toggle("hero-align-left", !original.center);
            shader.content.textContent = original.shader;
            document.documentElement.dataset.theme = original.theme;
            editor.open = false;
            paused = false;
            controller.setPaused(false);
            controller.refresh(true);
            controller.configure();
            updateControls();
            layoutChanged();
        });
        field("copy").addEventListener("click", async () => {
            try {
                if (!navigator.clipboard) throw new Error("Clipboard access is unavailable.");
                await navigator.clipboard.writeText(field("yaml").value);
                status.textContent = "Hero YAML copied.";
            } catch (problem) {
                status.textContent = `Copy failed: ${problem.message} The YAML is selected; copy it manually.`;
                yamlOptions.open = true;
                field("yaml").focus();
                field("yaml").select();
            }
        });
        motion.addEventListener("change", updateStatus);
        window.addEventListener("pagehide", () => {
            clearTimeout(shaderTimer);
            shaderTimer = 0;
        });
        window.addEventListener("pageshow", () => {
            if (shaderPending) previewShader();
        });
        updateControls();
        layoutChanged();
    });
})();
