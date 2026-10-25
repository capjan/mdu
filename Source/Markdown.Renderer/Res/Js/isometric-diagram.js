// Plays the phases of isometric diagrams one after another. The phase controls work without this script.
(() => {
    const stepDuration = 2600;

    document.querySelectorAll(".iso").forEach((diagram) => {
        const button = diagram.querySelector(".iso-play");
        const inputs = Array.from(diagram.querySelectorAll(".iso-step input"));
        if (!button || inputs.length < 2) return;

        const text = button.querySelector(".iso-play-text");
        let timer = null;

        // The label names the next action, so the button has no pressed state that would contradict it.
        const setPlaying = (playing) => {
            button.classList.toggle("is-playing", playing);
            text.textContent = playing ? "Pause" : "Play";
        };

        const stop = () => {
            window.clearInterval(timer);
            timer = null;
            setPlaying(false);
        };

        const current = () => inputs.findIndex((input) => input.checked);

        button.addEventListener("click", () => {
            if (timer) {
                stop();
                return;
            }

            if (current() === inputs.length - 1) inputs[0].checked = true;
            setPlaying(true);
            timer = window.setInterval(() => {
                const next = current() + 1;
                inputs[next].checked = true;
                if (next === inputs.length - 1) stop();
            }, stepDuration);
        });

        inputs.forEach((input) => input.addEventListener("change", stop));
        button.hidden = false;
        setPlaying(false);
    });
})();
