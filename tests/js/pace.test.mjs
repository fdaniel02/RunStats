import { test } from "node:test";
import assert from "node:assert/strict";
import { MaxPace, MinPace, formatDuration, formatGap, formatKm, formatSpeed, parseDuration, parseKm, parseStart, setupCalculator } from "../../src/RunStats/wwwroot/js/pace.js";

class FakeElement {
    value = "";
    min = "";
    max = "";
    textContent = "";
    hidden = false;
    attributes = {};
    listeners = {};

    constructor(dataset = {}) {
        this.dataset = dataset;
    }

    addEventListener(type, listener) {
        this.listeners[type] = listener;
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }

    type(text) {
        this.value = text;
        this.listeners.input();
    }

    leave() {
        this.listeners.change();
    }

    click() {
        this.listeners.click();
    }
}

const preset = meters => new FakeElement({ meters: String(meters) });
const reference = (kind, meters, seconds) => new FakeElement({ kind, meters: String(meters), seconds: String(seconds) });

function calculator(references = [], search = "") {
    const ids = ["distance", "time", "time-slider", "pace", "pace-slider", "speed", "gap-prediction", "gap-record"];
    const parts = Object.fromEntries(ids.map(id => [id, new FakeElement()]));
    const presets = [preset(5000), preset(16093.44), preset(42195)];
    setupCalculator({
        getElementById: id => parts[id.slice("pace-".length)],
        querySelectorAll: selector => (selector === ".distance-preset" ? presets : references)
    }, search);
    return { ...parts, presets, references };
}

test("durations can be typed with hours, with minutes, or as plain minutes", () => {
    assert.equal(parseDuration("1:45:30"), 6330);
    assert.equal(parseDuration(" 45:30 "), 2730);
    assert.equal(parseDuration("75:00"), 4500);
    assert.equal(parseDuration("45"), 2700);
});

test("unfinished or impossible durations aren't read", () => {
    for (const text of ["", "abc", "5:", "5:3", "5:60", "1:60:00", "1:2:3:4", "0", "0:00", "-5"])
        assert.equal(parseDuration(text), null, text);
});

test("distances take a point or a comma", () => {
    assert.equal(parseKm("16.1"), 16.1);
    assert.equal(parseKm("16,1"), 16.1);
    assert.equal(parseKm(" 5 "), 5);
    assert.equal(parseKm(".5"), 0.5);
    assert.equal(parseKm("5."), 5);
});

test("anything but a positive distance isn't read", () => {
    for (const text of ["", ".", "0", "abc", "-1", "1.2.3", "1e3"])
        assert.equal(parseKm(text), null, text);
});

test("durations show hours only from an hour", () => {
    assert.equal(formatDuration(65), "1:05");
    assert.equal(formatDuration(3599.6), "1:00:00");
    assert.equal(formatDuration(6330), "1:45:30");
});

test("gaps carry their sign", () => {
    assert.equal(formatGap(47), "+0:47");
    assert.equal(formatGap(-75), "-1:15");
    assert.equal(formatGap(0.2), "±0:00");
});

test("distances and speed are readable", () => {
    assert.equal(formatKm(16093.44), "16.0934");
    assert.equal(formatKm(42195), "42.195");
    assert.equal(formatKm(5000), "5");
    assert.equal(formatSpeed(360), "10.0");
});

test("without a prediction it starts on 5 km at 6:00", () => {
    const page = calculator();

    assert.equal(page.distance.value, "5");
    assert.equal(page.time.value, "30:00");
    assert.equal(page.pace.value, "6:00");
    assert.equal(page.speed.textContent, "10.0");
    assert.equal(page["pace-slider"].value, "360");
    assert.equal(page["pace-slider"].min, "150");
    assert.equal(page["pace-slider"].max, "600");
    assert.equal(page["time-slider"].value, "1800");
    assert.equal(page["time-slider"].min, String(MinPace * 5));
    assert.equal(page["time-slider"].max, String(MaxPace * 5));
    assert.deepEqual(page.presets.map(p => p.attributes["aria-pressed"]), ["true", "false", "false"]);
});

test("it starts at Garmin's 5 km prediction when there is one", () => {
    const page = calculator([reference("prediction", 10000, 2806), reference("prediction", 5000, 1310)]);

    assert.equal(page.time.value, "21:50");
    assert.equal(page.pace.value, "4:22");
});

test("a personal record isn't a starting point", () => {
    assert.equal(calculator([reference("record", 5000, 1200)]).time.value, "30:00");
});

test("typing a time sets the pace, and leaves the typed text alone", () => {
    const page = calculator();

    page.time.type("25:00");

    assert.equal(page.pace.value, "5:00");
    assert.equal(page["pace-slider"].value, "300");
    assert.equal(page["time-slider"].value, "1500");
    assert.equal(page.speed.textContent, "12.0");

    page.time.type("25");
    assert.equal(page.time.value, "25");
    page.time.leave();
    assert.equal(page.time.value, "25:00");
});

test("typing a pace sets the time", () => {
    const page = calculator();

    page.pace.type("4:30");

    assert.equal(page.time.value, "22:30");
    assert.equal(page["time-slider"].value, "1350");
});

test("the sliders set each other", () => {
    const page = calculator();

    page["pace-slider"].type("300");
    assert.equal(page.pace.value, "5:00");
    assert.equal(page.time.value, "25:00");
    assert.equal(page["time-slider"].value, "1500");

    page["time-slider"].type("1200");
    assert.equal(page.pace.value, "4:00");
    assert.equal(page.time.value, "20:00");
    assert.equal(page["pace-slider"].value, "240");
});

test("changing the distance keeps the pace and moves the time slider's range", () => {
    const page = calculator();

    page.distance.type("10");

    assert.equal(page.pace.value, "6:00");
    assert.equal(page.time.value, "1:00:00");
    assert.equal(page["time-slider"].min, String(MinPace * 10));
    assert.equal(page["time-slider"].max, String(MaxPace * 10));
    assert.deepEqual(page.presets.map(p => p.attributes["aria-pressed"]), ["false", "false", "false"]);
});

test("a preset sets the distance and is marked, also when typed", () => {
    const page = calculator();

    page.presets[1].click();

    assert.equal(page.distance.value, "16.0934");
    assert.equal(page.time.value, "1:36:34");
    assert.deepEqual(page.presets.map(p => p.attributes["aria-pressed"]), ["false", "true", "false"]);

    page.presets[0].click();
    page.distance.type("16,0934");
    assert.deepEqual(page.presets.map(p => p.attributes["aria-pressed"]), ["false", "true", "false"]);
});

test("the time can be typed beyond the slider's range", () => {
    const page = calculator();

    page.time.type("1:00:00");

    assert.equal(page.pace.value, "12:00");
    assert.equal(page.speed.textContent, "5.0");
    assert.equal(page["time-slider"].value, "3600");
});

test("a very short distance still has a usable time slider", () => {
    const page = calculator();

    page.distance.type("0.001");

    assert.equal(page["time-slider"].min, "1");
    assert.equal(page["time-slider"].max, "1");
});

test("unreadable text changes nothing and goes back when leaving the field", () => {
    const page = calculator();

    page.time.type("abc");
    assert.equal(page.pace.value, "6:00");
    page.time.leave();
    assert.equal(page.time.value, "30:00");

    page.distance.type("0");
    assert.equal(page.time.value, "30:00");
    page.distance.leave();
    assert.equal(page.distance.value, "5");

    page.pace.type("");
    page.pace.leave();
    assert.equal(page.pace.value, "6:00");
});

test("gaps show only for the selected distance", () => {
    const page = calculator([reference("prediction", 5000, 1310), reference("record", 5000, 1250), reference("record", 1000, 200)]);

    assert.equal(page["gap-prediction"].hidden, false);
    assert.equal(page["gap-prediction"].textContent, "Garmin predicts 21:50 (±0:00)");
    assert.equal(page["gap-record"].textContent, "Personal record 20:50 (+1:00)");

    page.pace.type("5:00");
    assert.equal(page["gap-prediction"].textContent, "Garmin predicts 21:50 (+3:10)");

    page.distance.type("1");
    assert.equal(page["gap-prediction"].hidden, true);
    assert.equal(page["gap-record"].textContent, "Personal record 3:20 (+1:40)");

    page.distance.type("7");
    assert.equal(page["gap-record"].hidden, true);
});

test("clicking a prediction or record loads its distance and time", () => {
    const page = calculator([reference("prediction", 10000, 2806), reference("record", 1000, 200)]);

    page.references[0].click();
    assert.equal(page.distance.value, "10");
    assert.equal(page.time.value, "46:46");
    assert.equal(page.pace.value, "4:41");

    page.references[1].click();
    assert.equal(page.distance.value, "1");
    assert.equal(page.time.value, "3:20");
});

test("a race's link starts it on the race's distance and time", () => {
    const page = calculator([reference("prediction", 5000, 1310)], "?meters=16100&seconds=4800");

    assert.equal(page.distance.value, "16.1");
    assert.equal(page.time.value, "1:20:00");
    assert.equal(page.pace.value, "4:58");
});

test("a race's link without a time keeps the usual pace", () => {
    const page = calculator([reference("prediction", 5000, 1310)], "?meters=16100");

    assert.equal(page.distance.value, "16.1");
    assert.equal(page.pace.value, "4:22");
});

test("a time without a distance is taken for 5 km", () => {
    const page = calculator([], "?seconds=1500");

    assert.equal(page.distance.value, "5");
    assert.equal(page.time.value, "25:00");
});

test("unusable values in the link are ignored", () => {
    for (const search of ["?meters=abc&seconds=-5", "?meters=0&seconds=0", "?meters=Infinity", "?meters=&seconds="])
        assert.deepEqual(parseStart(search), { meters: null, seconds: null }, search);
    assert.deepEqual(parseStart(""), { meters: null, seconds: null });
});

test("the page's script starts the calculator only on the calculator page", async () => {
    const asked = [];
    globalThis.document = { getElementById: id => { asked.push(id); return null; } };
    try {
        await import("../../src/RunStats/wwwroot/js/pace.js?page");
    } finally {
        delete globalThis.document;
    }

    assert.deepEqual(asked, ["pace-calculator"]);
});
