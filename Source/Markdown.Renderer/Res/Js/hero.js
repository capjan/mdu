(() => {
    const api = window.mduHero ??= { effects: Object.create(null), controllers: new WeakMap() };
    const root = document.documentElement;
    const systemDark = matchMedia("(prefers-color-scheme: dark)");
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const vertexSource = "attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}";
    const shaderHeader = `
precision highp float;
uniform vec3 iResolution;
uniform float iTime, iDark;
uniform vec4 iMouse;
uniform vec3 iBackground, iForeground, iAccent;
#line 1
`;
    const colorCanvas = document.createElement("canvas");
    colorCanvas.width = colorCanvas.height = 1;
    const colorContext = colorCanvas.getContext("2d", { willReadFrequently: true });
    const readColor = (value) => {
        if (!colorContext) throw new Error("Cannot read the hero theme colors.");
        colorContext.clearRect(0, 0, 1, 1);
        colorContext.fillStyle = value;
        colorContext.fillRect(0, 0, 1, 1);
        return Array.from(colorContext.getImageData(0, 0, 1, 1).data).slice(0, 3).map(c => c / 255);
    };
    const palette = (hero) => {
        const style = getComputedStyle(hero);
        const accent = document.createElement("span");
        accent.hidden = true;
        accent.style.color = hero.dataset.heroAccent || "var(--link)";
        hero.append(accent);
        const accentColor = getComputedStyle(accent).color;
        accent.remove();
        return {
            dark: root.dataset.theme === "dark" || root.dataset.theme !== "light" && systemDark.matches,
            background: readColor(style.backgroundColor),
            foreground: readColor(style.color),
            accent: readColor(accentColor)
        };
    };
    const mix = (from, to, amount) => {
        const result = {};
        for (const [name, values] of Object.entries(to)) {
            result[name] = name === "uSunSize" ? (amount < 0.5 ? from[name] : values)
                : values.map((value, i) => from[name][i] + (value - from[name][i]) * amount);
        }
        if (result.uSun) {
            const length = Math.hypot(...result.uSun);
            result.uSun = result.uSun.map(value => value / length);
        }
        return result;
    };

    const init = (hero) => {
        const canvas = hero.querySelector(".hero-canvas");
        const error = hero.querySelector(".hero-error");
        hero.after(error);
        let gl, program, buffer, activeEffect, locations = Object.create(null);
        let visible = false, paused = false, failed = !error.hidden, lost = false;
        let needsRefresh = false;
        let frame = 0, lastFrame = null, sceneTime = 14, introStart = null;
        let scale = hero.dataset.heroQuality === "high" ? 1 : hero.dataset.heroQuality === "low" ? 0.5 : 0.75;
        let sampledFrames = 0, slowFrames = 0;
        let current, from, target, blendStart = 0;
        let look = [0, 0], lookTarget = [0, 0], mouse = [0, 0, 0, 0];
        const speed = () => Number(hero.dataset.heroSpeed);
        const still = () => paused || speed() === 0 || reducedMotion.matches;
        const stop = () => {
            if (frame) cancelAnimationFrame(frame);
            frame = 0;
            lastFrame = null;
        };
        const dispose = () => {
            stop();
            if (program) gl.deleteProgram(program);
            if (buffer) gl.deleteBuffer(buffer);
            program = buffer = null;
            locations = Object.create(null);
        };
        const fail = (message, preserve = false) => {
            if (preserve && program && !lost) {
                message += "\nThe last valid animation remains visible.";
            } else {
                dispose();
                failed = true;
                hero.classList.remove("hero-ready");
            }
            error.textContent = message;
            error.hidden = false;
            console.warn("mdu hero:", message);
            hero.dispatchEvent(new CustomEvent("mdu:hero-error", { detail: message }));
        };
        const uniforms = (effect = activeEffect) => {
            const colors = palette(hero);
            return {
                iBackground: colors.background, iForeground: colors.foreground, iAccent: colors.accent,
                iDark: [colors.dark ? 1 : 0],
                ...(api.effects[effect]?.uniforms?.(hero, colors) || {})
            };
        };
        const set = (name, value) => {
            if (!(name in locations)) locations[name] = gl.getUniformLocation(program, name);
            const location = locations[name];
            if (location === null) return;
            if (value.length === 1) gl.uniform1fv(location, value);
            else if (value.length === 2) gl.uniform2fv(location, value);
            else if (value.length === 3) gl.uniform3fv(location, value);
            else gl.uniform4fv(location, value);
        };
        const resize = () => {
            const ratio = Math.min(devicePixelRatio || 1, 2) * scale;
            const width = Math.max(1, Math.round(canvas.clientWidth * ratio));
            const height = Math.max(1, Math.round(canvas.clientHeight * ratio));
            if (canvas.width === width && canvas.height === height) return false;
            canvas.width = width;
            canvas.height = height;
            gl.viewport(0, 0, width, height);
            return true;
        };
        const draw = (now) => {
            resize();
            const blend = still() || !blendStart ? 1 : Math.min(1, (now - blendStart) / 1400);
            current = blend < 1 ? mix(from, target, blend * blend * (3 - 2 * blend)) : target;
            gl.useProgram(program);
            for (const [name, value] of Object.entries(current)) set(name, value);
            look = look.map((value, i) => value + (lookTarget[i] - value) * 0.04);
            set("uIntro", [still() || introStart === null ? 1 : Math.min(1, (now - introStart) / 3200)]);
            set("uWave", [Number(hero.dataset.heroWaves)]);
            set("uLook", look);
            set("uTime", [sceneTime]);
            set("uRes", [canvas.width, canvas.height]);
            set("iTime", [sceneTime]);
            set("iResolution", [canvas.width, canvas.height, 1]);
            set("iMouse", [mouse[0] * canvas.width, mouse[1] * canvas.height, mouse[2], mouse[3]]);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
        };
        const schedule = () => {
            if (frame || !visible || document.hidden || !program || failed || lost) return;
            frame = requestAnimationFrame(now => {
                frame = 0;
                if (!still() && lastFrame !== null) {
                    const delta = Math.min(0.1, (now - lastFrame) / 1000);
                    sceneTime += delta * speed();
                    if (hero.dataset.heroQuality === "auto" && sampledFrames < 90) {
                        sampledFrames++;
                        if (delta > 1 / 40) slowFrames++;
                        if (sampledFrames === 90 && slowFrames > 45) scale = 0.5;
                    }
                }
                lastFrame = still() ? null : now;
                draw(now);
                if (!still()) schedule();
            });
        };
        const compile = (type, source) => {
            const shader = gl.createShader(type);
            if (!shader) throw new Error("Cannot allocate a hero shader.");
            gl.shaderSource(shader, source);
            gl.compileShader(shader);
            if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
                const message = gl.getShaderInfoLog(shader);
                gl.deleteShader(shader);
                throw new Error(message || "Hero shader compilation failed.");
            }
            return shader;
        };
        const prepare = (force = false) => {
            if (program && !needsRefresh || failed || lost || !visible && !force || document.hidden) return;
            let nextProgram, nextBuffer;
            try {
                gl ??= canvas.getContext("webgl", { antialias: false, alpha: false, depth: false });
                if (!gl) throw new Error("WebGL is unavailable. Showing a static hero background.");
                const effect = hero.dataset.heroScene;
                let source = api.effects[effect]?.fragment;
                if (effect === "shader") {
                    const code = hero.querySelector(".hero-shader")?.content.textContent || "";
                    if (!code.trim()) throw new Error("The shader effect needs GLSL code in the shader option.");
                    if (code.length > 32768) throw new Error("The hero shader must not exceed 32768 characters.");
                    source = shaderHeader + code + "\nvoid main(){mainImage(gl_FragColor,gl_FragCoord.xy);}";
                }
                if (!source) throw new Error(`Unknown hero effect: ${effect}.`);
                nextProgram = gl.createProgram();
                if (!nextProgram) throw new Error("Cannot allocate a hero program.");
                const shaders = [];
                try {
                    for (const [type, code] of [[gl.VERTEX_SHADER, vertexSource], [gl.FRAGMENT_SHADER, source]]) {
                        const shader = compile(type, code);
                        shaders.push(shader);
                        gl.attachShader(nextProgram, shader);
                    }
                    gl.linkProgram(nextProgram);
                    if (!gl.getProgramParameter(nextProgram, gl.LINK_STATUS))
                        throw new Error(gl.getProgramInfoLog(nextProgram) || "Hero shader linking failed.");
                } finally {
                    for (const shader of shaders) {
                        gl.detachShader(nextProgram, shader);
                        gl.deleteShader(shader);
                    }
                }
                nextBuffer = gl.createBuffer();
                if (!nextBuffer) throw new Error("Cannot allocate a hero vertex buffer.");
                const nextUniforms = uniforms(effect);
                const previousProgram = program, previousBuffer = buffer;
                gl.useProgram(nextProgram);
                gl.bindBuffer(gl.ARRAY_BUFFER, nextBuffer);
                gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
                const position = gl.getAttribLocation(nextProgram, "p");
                gl.enableVertexAttribArray(position);
                gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
                program = nextProgram;
                buffer = nextBuffer;
                nextProgram = nextBuffer = null;
                activeEffect = effect;
                locations = Object.create(null);
                current = from = target = nextUniforms;
                blendStart = 0;
                if (!previousProgram) introStart = performance.now();
                if (previousProgram) gl.deleteProgram(previousProgram);
                if (previousBuffer) gl.deleteBuffer(previousBuffer);
                error.hidden = true;
                error.textContent = "";
                draw(performance.now());
                hero.classList.add("hero-ready");
                hero.dispatchEvent(new CustomEvent("mdu:hero-ready"));
            } catch (problem) {
                fail(`Hero effect could not start: ${problem.message}`, true);
            } finally {
                if (nextProgram) gl.deleteProgram(nextProgram);
                if (nextBuffer) gl.deleteBuffer(nextBuffer);
                needsRefresh = false;
            }
        };
        const refresh = (resetTime = false) => {
            failed = false;
            needsRefresh = true;
            if (resetTime) sceneTime = 14;
            prepare(true);
            schedule();
        };
        const configure = () => {
            scale = hero.dataset.heroQuality === "high" ? 1 : hero.dataset.heroQuality === "low" ? 0.5 : 0.75;
            sampledFrames = slowFrames = 0;
            if (program && !lost && !failed) {
                current = from = target = uniforms();
                blendStart = 0;
                if (!document.hidden) draw(performance.now());
            }
            schedule();
        };
        const themeChanged = () => {
            if (program && !lost && !failed) {
                from = current;
                target = uniforms();
                blendStart = activeEffect === "beach" ? performance.now() : 0;
            }
            schedule();
        };
        const wake = () => {
            if (document.hidden) stop();
            else { prepare(); schedule(); }
        };
        const controller = {
            refresh, configure, themeChanged, wake, stop, error,
            accentColor() { return "#" + palette(hero).accent.map(value => Math.round(value * 255).toString(16).padStart(2, "0")).join(""); },
            beachColors() { return api.effects.beach?.colors(hero, palette(hero)); },
            setPaused(value) { paused = value; stop(); schedule(); }
        };
        api.controllers.set(hero, controller);
        if (failed) console.warn("mdu hero:", error.textContent);
        const visibility = new IntersectionObserver(entries => {
            visible = entries.some(entry => entry.isIntersecting);
            if (visible) wake(); else stop();
        });
        visibility.observe(hero);
        new ResizeObserver(() => {
            if (!program || !visible || document.hidden || lost || failed) return;
            if (resize()) draw(performance.now());
        }).observe(canvas);
        hero.addEventListener("pointermove", event => {
            if (event.pointerType !== "mouse" || still()) return;
            const bounds = hero.getBoundingClientRect();
            const x = (event.clientX - bounds.left) / bounds.width;
            const y = 1 - (event.clientY - bounds.top) / bounds.height;
            lookTarget = [x * 2 - 1, y * 2 - 1];
            mouse = [x, y, 0, 0];
        });
        hero.addEventListener("pointerleave", () => { lookTarget = [0, 0]; mouse = [0, 0, 0, 0]; });
        canvas.addEventListener("webglcontextlost", event => {
            event.preventDefault();
            lost = true;
            fail("The hero graphics context was lost. Waiting for the browser to restore it.");
        });
        canvas.addEventListener("webglcontextrestored", () => { lost = false; refresh(); });
        return controller;
    };
    const controllers = Array.from(document.querySelectorAll(".hero[data-hero-scene]"), init);
    const updateTheme = () => controllers.forEach(controller => controller.themeChanged());
    new MutationObserver(updateTheme).observe(root, { attributes: true, attributeFilter: ["data-theme"] });
    systemDark.addEventListener("change", updateTheme);
    reducedMotion.addEventListener("change", () => controllers.forEach(controller => { controller.stop(); controller.wake(); }));
    document.addEventListener("visibilitychange", () => controllers.forEach(controller => controller.wake()));
    window.addEventListener("pagehide", () => controllers.forEach(controller => controller.stop()));
    window.addEventListener("pageshow", () => controllers.forEach(controller => controller.wake()));
})();
