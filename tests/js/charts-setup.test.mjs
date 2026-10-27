import { test } from "node:test";
import assert from "node:assert/strict";
import { setup } from "../../src/RunStats/wwwroot/js/charts.js";

// Just enough of the DOM for setup(): listeners, classes, styles and fixed boxes.
class FakeElement {
    constructor(rect = { left: 0, width: 0 }) {
        this.rect = rect;
        this.style = {};
        this.hidden = true;
        this.children = [];
        this.listeners = {};
        this.offsetWidth = 150;
        this.classes = new Set();
        this.classList = { add: c => this.classes.add(c), remove: c => this.classes.delete(c) };
    }

    addEventListener(type, listener) {
        (this.listeners[type] ??= []).push(listener);
    }

    async fire(type, event = {}) {
        const fired = { prevented: false, preventDefault() { this.prevented = true; }, ...event };
        for (const listener of this.listeners[type] ?? [])
            await listener(fired);
        return fired;
    }

    getBoundingClientRect() {
        return this.rect;
    }

    replaceChildren(...children) {
        this.children = children;
    }

    append(...children) {
        this.children.push(...children);
    }
}

const data = {
    total: 4,
    series: [{ key: "pace", title: "Pace", unit: "/km" }, { key: "hr", title: "Heart rate", unit: "bpm" }],
    km: [0, 1, 2, 3, 4],
    time: [0, 300, 600, 900, 1200],
    pace: [330, 320, 310, 300, 290],
    hr: [120, 130, 140, 150, 160],
    lat: [51.6, 51.61, null, 51.63, 51.64],
    lon: [5.5, 5.51, null, 5.53, 5.54]
};

function page({ requestFullscreen, lock, exitFullscreen } = {}) {
    const drawings = [new FakeElement({ left: 100, width: 400 }), new FakeElement({ left: 100, width: 400 })];
    const cursors = [new FakeElement(), new FakeElement()];
    const parts = {
        ".plots": new FakeElement({ left: 50, width: 450 }),
        ".chart-tip": new FakeElement(),
        ".drawing": drawings[0],
        ".fullscreen-open": new FakeElement(),
        ".fullscreen-close": new FakeElement()
    };
    const root = new FakeElement();
    root.querySelector = selector => parts[selector];
    root.querySelectorAll = selector => (selector === ".cursor" ? cursors : drawings);
    root.contains = element => element === parts[".fullscreen-open"];
    if (requestFullscreen)
        root.requestFullscreen = requestFullscreen;

    const positions = [];
    const calls = [];
    const document = new FakeElement();
    document.createElement = () => new FakeElement();
    document.dispatchEvent = e => positions.push(e.detail);
    document.fullscreenElement = null;
    document.exitFullscreen = exitFullscreen ?? (() => {
        calls.push("exit");
        return Promise.resolve();
    });
    const screen = {
        orientation: {
            lock: lock ?? (orientation => {
                calls.push(`lock ${orientation}`);
                return Promise.resolve();
            }),
            unlock: () => calls.push("unlock")
        }
    };

    setup(root, data, { document, screen });
    return { root, drawings, cursors, tip: parts[".chart-tip"], parts, document, positions, calls };
}

const rows = tip => tip.children.map(row => row.children.map(c => c.textContent).join(" "));

test("hovering shows the line in every chart, the values and the map position", async () => {
    const { drawings, cursors, tip, positions } = page();

    await drawings[1].fire("pointermove", { clientX: 200 });

    assert.deepEqual(cursors.map(c => [c.hidden, c.style.left]), [[false, "25%"], [false, "25%"]]);
    assert.equal(tip.hidden, false);
    assert.deepEqual(rows(tip), ["Distance 1.00 km", "Time 5:00", "Pace 5:20 /km", "Heart rate 130 bpm"]);
    assert.deepEqual(positions, [[51.61, 5.51]]);
});

test("the readout sits right of the line on the left half and left of it on the right half", async () => {
    const { drawings, tip } = page();

    await drawings[0].fire("pointermove", { clientX: 200 });
    assert.equal(tip.style.left, "162px");

    await drawings[0].fire("pointermove", { clientX: 400 });
    assert.equal(tip.style.left, "188px");
});

test("a pointer beyond the chart's edges picks the first or last point", async () => {
    const { drawings, tip } = page();

    await drawings[0].fire("pointerdown", { clientX: 20 });
    assert.equal(rows(tip)[0], "Distance 0.00 km");

    await drawings[0].fire("pointermove", { clientX: 900 });
    assert.equal(rows(tip)[0], "Distance 4.00 km");
});

test("a point without GPS has no map position", async () => {
    const { drawings, positions } = page();

    await drawings[0].fire("pointermove", { clientX: 300 });

    assert.deepEqual(positions, [null]);
});

test("the mouse leaving hides everything, a finger lifting keeps the readout", async () => {
    const { drawings, cursors, tip, positions } = page();
    await drawings[0].fire("pointermove", { clientX: 200 });

    await drawings[0].fire("pointerleave", { pointerType: "touch" });
    assert.equal(tip.hidden, false);

    await drawings[0].fire("pointerleave", { pointerType: "mouse" });
    assert.equal(tip.hidden, true);
    assert.ok(cursors.every(c => c.hidden));
    assert.equal(positions.at(-1), null);
});

test("the arrow keys step through the run, Shift by ten, Home and End jump", async () => {
    const { root, tip } = page();
    const distance = () => rows(tip)[0];

    assert.equal((await root.fire("keydown", { key: "ArrowRight" })).prevented, true);
    assert.equal(distance(), "Distance 0.00 km");
    await root.fire("keydown", { key: "ArrowRight" });
    assert.equal(distance(), "Distance 1.00 km");
    await root.fire("keydown", { key: "ArrowRight", shiftKey: true });
    assert.equal(distance(), "Distance 4.00 km");
    await root.fire("keydown", { key: "ArrowLeft" });
    assert.equal(distance(), "Distance 3.00 km");
    await root.fire("keydown", { key: "Home" });
    assert.equal(distance(), "Distance 0.00 km");
    await root.fire("keydown", { key: "End" });
    assert.equal(distance(), "Distance 4.00 km");
});

test("ArrowLeft without a readout starts at the end", async () => {
    const { root, tip } = page();

    await root.fire("keydown", { key: "ArrowLeft" });

    assert.equal(rows(tip)[0], "Distance 4.00 km");
});

test("other keys are left alone and Escape hides the readout", async () => {
    const { root, tip } = page();
    await root.fire("keydown", { key: "End" });

    assert.equal((await root.fire("keydown", { key: "a" })).prevented, false);
    assert.equal(tip.hidden, false);
    await root.fire("keydown", { key: "Escape" });
    assert.equal(tip.hidden, true);
});

test("focus leaving the charts hides the readout, focus moving inside keeps it", async () => {
    const { root, tip, parts } = page();
    await root.fire("keydown", { key: "Home" });

    await root.fire("focusout", { relatedTarget: parts[".fullscreen-open"] });
    assert.equal(tip.hidden, false);

    await root.fire("focusout", { relatedTarget: null });
    assert.equal(tip.hidden, true);
});

test("full screen asks for landscape", async () => {
    const calls = [];
    const { root, parts, calls: screenCalls } = page({ requestFullscreen: options => {
        calls.push(options);
        return Promise.resolve();
    } });

    await parts[".fullscreen-open"].fire("click");

    assert.deepEqual(calls, [{ navigationUI: "hide" }]);
    assert.deepEqual(screenCalls, ["lock landscape"]);
    assert.ok(root.classes.has("is-fullscreen"));
});

test("when landscape is refused the charts still go full screen", async () => {
    const { root, parts } = page({ requestFullscreen: () => Promise.resolve(), lock: () => Promise.reject(new Error("not allowed")) });

    await parts[".fullscreen-open"].fire("click");

    assert.ok(root.classes.has("is-fullscreen"));
});

test("without the Fullscreen API the charts fill the window", async () => {
    const { root, parts, calls } = page();

    await parts[".fullscreen-open"].fire("click");

    assert.ok(root.classes.has("is-fullscreen"));
    assert.deepEqual(calls, []);
});

test("closing leaves full screen", async () => {
    const { root, parts, document, calls } = page({ requestFullscreen: () => Promise.resolve() });
    await parts[".fullscreen-open"].fire("click");
    document.fullscreenElement = root;

    await parts[".fullscreen-close"].fire("click");

    assert.ok(calls.includes("exit"));
    assert.ok(!root.classes.has("is-fullscreen"));
});

test("closing after full screen already ended only resets the layout", async () => {
    const { root, parts, calls } = page();
    await parts[".fullscreen-open"].fire("click");

    await parts[".fullscreen-close"].fire("click");

    assert.ok(!calls.includes("exit"));
    assert.ok(!root.classes.has("is-fullscreen"));
});

test("a failed exit is ignored", async () => {
    const { root, parts, document } = page({ requestFullscreen: () => Promise.resolve(), exitFullscreen: () => Promise.reject(new Error("not in full screen")) });
    document.fullscreenElement = root;

    await parts[".fullscreen-close"].fire("click");
    await new Promise(resolve => setImmediate(resolve));

    assert.ok(!root.classes.has("is-fullscreen"));
});

test("leaving full screen another way resets the layout and the orientation", async () => {
    const { root, parts, document, calls } = page({ requestFullscreen: () => Promise.resolve() });
    await parts[".fullscreen-open"].fire("click");

    document.fullscreenElement = root;
    await document.fire("fullscreenchange");
    assert.ok(root.classes.has("is-fullscreen"));

    document.fullscreenElement = null;
    await document.fire("fullscreenchange");
    assert.ok(!root.classes.has("is-fullscreen"));
    assert.equal(calls.at(-1), "unlock");
});
