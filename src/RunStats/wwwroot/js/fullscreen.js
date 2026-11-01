// Real full screen turned to landscape where the browser allows it (Firefox and Chrome on Android); otherwise the
// element just fills the window, and in portrait a hint asks to turn the phone.
export function setupFullscreen(root, document, screen) {
    root.querySelector(".fullscreen-open").addEventListener("click", async () => {
        if (root.requestFullscreen) {
            try {
                await root.requestFullscreen({ navigationUI: "hide" });
                await screen.orientation?.lock?.("landscape");
            } catch {
                // Turning is up to the person then; the hint shows in portrait.
            }
        }
        root.classList.add("is-fullscreen");
    });
    root.querySelector(".fullscreen-close").addEventListener("click", () => {
        if (document.fullscreenElement)
            document.exitFullscreen().catch(() => {
                // Already left, e.g. with the back gesture.
            });
        root.classList.remove("is-fullscreen");
    });
    document.addEventListener("fullscreenchange", () => {
        if (document.fullscreenElement)
            return;
        root.classList.remove("is-fullscreen");
        try {
            screen.orientation?.unlock?.();
        } catch {
            // Nothing was locked.
        }
    });
}
