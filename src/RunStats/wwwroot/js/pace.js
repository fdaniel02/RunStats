// The calculation runs in the browser, so the sliders follow the finger without a round trip to the server.

export const MinPace = 150;
export const MaxPace = 600;
const DefaultPace = 360;
const StartMeters = 5000;
const SameDistanceMeters = 1;

// "1:45:30", "45:30", or "45" for minutes. Seconds need both digits, so "5:3" on the way to "5:30" isn't taken as 5:03.
export function parseDuration(text) {
    const trimmed = text.trim();
    if (!/^\d+(:\d{2}){0,2}$/.test(trimmed))
        return null;
    const parts = trimmed.split(":").map(Number);
    if (parts.slice(1).some(part => part >= 60))
        return null;
    const seconds = parts.length === 1 ? parts[0] * 60 : parts.reduce((total, part) => total * 60 + part, 0);
    return seconds > 0 ? seconds : null;
}

export function parseKm(text) {
    const trimmed = text.trim().replace(",", ".");
    if (!/^(\d+(\.\d*)?|\.\d+)$/.test(trimmed))
        return null;
    const km = Number(trimmed);
    return km > 0 ? km : null;
}

export function formatDuration(seconds) {
    const total = Math.round(seconds);
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor(total % 3600 / 60);
    const rest = String(total % 60).padStart(2, "0");
    return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${rest}` : `${minutes}:${rest}`;
}

export function formatGap(seconds) {
    const rounded = Math.round(seconds);
    if (rounded === 0)
        return "±0:00";
    return (rounded > 0 ? "+" : "-") + formatDuration(Math.abs(rounded));
}

export const formatKm = meters => String(Number((meters / 1000).toFixed(4)));

export const formatSpeed = secondsPerKm => (3600 / secondsPerKm).toFixed(1);

const positiveNumber = text => {
    const number = Number(text);
    return text !== null && number > 0 && Number.isFinite(number) ? number : null;
};

// A race on the dashboard links here with its distance and time.
export function parseStart(search) {
    const query = new URLSearchParams(search);
    return { meters: positiveNumber(query.get("meters")), seconds: positiveNumber(query.get("seconds")) };
}

export function setupCalculator(document, search = "") {
    const element = id => document.getElementById(id);
    const distanceInput = element("pace-distance");
    const timeInput = element("pace-time");
    const timeSlider = element("pace-time-slider");
    const paceInput = element("pace-pace");
    const paceSlider = element("pace-pace-slider");
    paceSlider.min = String(MinPace);
    paceSlider.max = String(MaxPace);
    const speed = element("pace-speed");
    const gaps = { prediction: element("pace-gap-prediction"), record: element("pace-gap-record") };
    const gapLabels = { prediction: "Garmin predicts", record: "Personal record" };
    const presets = [...document.querySelectorAll(".distance-preset")];
    const references = [...document.querySelectorAll(".pace-reference")];

    const start = references.find(r => r.dataset.kind === "prediction" && Number(r.dataset.meters) === StartMeters);
    const requested = parseStart(search);
    let meters = requested.meters ?? StartMeters;
    let secondsPerKm = start ? Number(start.dataset.seconds) / (StartMeters / 1000) : DefaultPace;
    if (requested.seconds !== null)
        secondsPerKm = requested.seconds / (meters / 1000);

    const isSelected = button => Math.abs(Number(button.dataset.meters) - meters) < SameDistanceMeters;

    function renderGap(kind, time) {
        const reference = references.find(r => r.dataset.kind === kind && isSelected(r));
        gaps[kind].hidden = !reference;
        if (reference) {
            const seconds = Number(reference.dataset.seconds);
            gaps[kind].textContent = `${gapLabels[kind]} ${formatDuration(seconds)} (${formatGap(time - seconds)})`;
        }
    }

    // The field being typed in or dragged is left alone: rewriting it would move the cursor or the thumb.
    function render(edited) {
        const km = meters / 1000;
        const time = secondsPerKm * km;
        // The bounds come first: the browser clamps a value to the bounds it has at that moment.
        timeSlider.min = String(Math.max(1, Math.floor(MinPace * km)));
        timeSlider.max = String(Math.max(1, Math.ceil(MaxPace * km)));
        if (edited !== distanceInput) distanceInput.value = formatKm(meters);
        if (edited !== timeInput) timeInput.value = formatDuration(time);
        if (edited !== timeSlider) timeSlider.value = String(Math.round(time));
        if (edited !== paceInput) paceInput.value = formatDuration(secondsPerKm);
        if (edited !== paceSlider) paceSlider.value = String(Math.round(secondsPerKm));
        speed.textContent = formatSpeed(secondsPerKm);
        for (const preset of presets)
            preset.setAttribute("aria-pressed", String(isSelected(preset)));
        renderGap("prediction", time);
        renderGap("record", time);
    }

    function setTime(seconds) {
        secondsPerKm = seconds / (meters / 1000);
    }

    function listen(input, apply) {
        input.addEventListener("input", () => {
            const applied = apply(input.value);
            if (applied)
                render(input);
        });
    }

    listen(distanceInput, text => {
        const km = parseKm(text);
        if (km !== null) meters = km * 1000;
        return km !== null;
    });
    listen(timeInput, text => {
        const seconds = parseDuration(text);
        if (seconds !== null) setTime(seconds);
        return seconds !== null;
    });
    listen(paceInput, text => {
        const seconds = parseDuration(text);
        if (seconds !== null) secondsPerKm = seconds;
        return seconds !== null;
    });
    listen(timeSlider, value => {
        setTime(Number(value));
        return true;
    });
    listen(paceSlider, value => {
        secondsPerKm = Number(value);
        return true;
    });

    // Leaving a field tidies what was typed, and puts the last valid value back over anything unreadable.
    for (const input of [distanceInput, timeInput, paceInput])
        input.addEventListener("change", () => render());

    for (const preset of presets) {
        preset.addEventListener("click", () => {
            meters = Number(preset.dataset.meters);
            render();
        });
    }
    for (const reference of references) {
        reference.addEventListener("click", () => {
            meters = Number(reference.dataset.meters);
            setTime(Number(reference.dataset.seconds));
            render();
        });
    }

    render();
}

if (typeof document !== "undefined" && document.getElementById("pace-calculator"))
    setupCalculator(document, location.search);
