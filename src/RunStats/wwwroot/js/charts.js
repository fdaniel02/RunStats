// The page puts the chart points into #chart-data as JSON. The route map listens for "run:position" to move its dot.

export function nearestIndex(kms, km) {
    let low = 0, high = kms.length - 1;
    while (high - low > 1) {
        const middle = (low + high) >> 1;
        if (kms[middle] <= km)
            low = middle;
        else
            high = middle;
    }
    return Math.abs(kms[high] - km) < Math.abs(kms[low] - km) ? high : low;
}

export function formatPace(secondsPerKm) {
    const seconds = Math.round(secondsPerKm);
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function formatValue(key, value) {
    if (key === "pace")
        return formatPace(value);
    if (key === "stride")
        return value.toFixed(2);
    return String(Math.round(value));
}

// The rows of the hover box at point i: the distance, then every chart's value; "–" where nothing was recorded.
export function readout(data, i) {
    return [
        { label: "Distance", value: `${data.km[i].toFixed(2)} km` },
        ...data.series.map(s => {
            const value = data[s.key][i];
            return { label: s.title, value: value == null ? "–" : `${formatValue(s.key, value)} ${s.unit}` };
        })
    ];
}

export function position(data, i) {
    return data.lat[i] == null || data.lon[i] == null ? null : [data.lat[i], data.lon[i]];
}

function setup(root, data) {
    const plots = root.querySelector(".plots");
    const tip = root.querySelector(".chart-tip");
    const cursors = [...root.querySelectorAll(".cursor")];
    const firstDrawing = root.querySelector(".drawing");
    let current = null;

    function show(i) {
        current = i;
        const share = data.km[i] / data.total;
        for (const cursor of cursors) {
            cursor.style.left = `${share * 100}%`;
            cursor.hidden = false;
        }
        // Built with textContent: the titles come from the page, but nothing here should ever be parsed as HTML.
        tip.replaceChildren(...readout(data, i).map(row => {
            const element = document.createElement("div");
            element.className = "tip-row";
            const label = document.createElement("span");
            label.className = "tip-label";
            label.textContent = row.label;
            const value = document.createElement("span");
            value.className = "tip-value";
            value.textContent = row.value;
            element.append(label, value);
            return element;
        }));
        tip.hidden = false;
        const drawing = firstDrawing.getBoundingClientRect();
        const box = plots.getBoundingClientRect();
        const x = drawing.left - box.left + share * drawing.width;
        // On the right half the box goes left of the line, so it never runs off the screen.
        tip.style.left = share > 0.5 ? `${x - tip.offsetWidth - 12}px` : `${x + 12}px`;
        document.dispatchEvent(new CustomEvent("run:position", { detail: position(data, i) }));
    }

    function hide() {
        current = null;
        cursors.forEach(c => c.hidden = true);
        tip.hidden = true;
        document.dispatchEvent(new CustomEvent("run:position", { detail: null }));
    }

    function showAt(clientX) {
        const drawing = firstDrawing.getBoundingClientRect();
        const share = Math.min(Math.max((clientX - drawing.left) / drawing.width, 0), 1);
        show(nearestIndex(data.km, share * data.total));
    }

    for (const drawing of root.querySelectorAll(".drawing")) {
        drawing.addEventListener("pointermove", e => showAt(e.clientX));
        drawing.addEventListener("pointerdown", e => showAt(e.clientX));
        drawing.addEventListener("pointerleave", e => {
            if (e.pointerType === "mouse")
                hide();
        });
    }

    root.addEventListener("keydown", e => {
        const last = data.km.length - 1;
        const step = e.shiftKey ? 10 : 1;
        const moves = {
            ArrowRight: i => Math.min((i ?? -1) + step, last),
            ArrowLeft: i => Math.max((i ?? last + 1) - step, 0),
            Home: () => 0,
            End: () => last
        };
        if (e.key === "Escape") {
            hide();
            return;
        }
        if (!moves[e.key])
            return;
        e.preventDefault();
        show(moves[e.key](current));
    });
    root.addEventListener("focusout", e => {
        if (!root.contains(e.relatedTarget))
            hide();
    });

    setupFullscreen(root);
}

// Real full screen turned to landscape where the browser allows it (Firefox and Chrome on Android); otherwise the
// charts just fill the window, and in portrait a hint asks to turn the phone.
function setupFullscreen(root) {
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
            document.exitFullscreen();
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

const root = typeof document === "undefined" ? null : document.getElementById("charts");
if (root)
    setup(root, JSON.parse(document.getElementById("chart-data").textContent));
