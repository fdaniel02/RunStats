// The year page's activity grid goes full screen like the run charts.
import { setupFullscreen } from "./fullscreen.js";

const root = typeof document === "undefined" ? null : document.getElementById("activity");
if (root)
    setupFullscreen(root, document, screen);
