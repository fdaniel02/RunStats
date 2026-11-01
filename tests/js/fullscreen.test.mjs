import { test } from "node:test";
import assert from "node:assert/strict";
import { setupFullscreen } from "../../src/RunStats/wwwroot/js/fullscreen.js";

class FakeElement {
    constructor() {
        this.listeners = {};
        this.classes = new Set();
        this.classList = { add: c => this.classes.add(c), remove: c => this.classes.delete(c) };
    }

    addEventListener(type, listener) {
        (this.listeners[type] ??= []).push(listener);
    }

    async fire(type, event = {}) {
        for (const listener of this.listeners[type] ?? [])
            await listener(event);
    }
}

function page({ requestFullscreen, lock, unlock, exitFullscreen } = {}) {
    const parts = { ".fullscreen-open": new FakeElement(), ".fullscreen-close": new FakeElement() };
    const root = new FakeElement();
    root.querySelector = selector => parts[selector];
    if (requestFullscreen)
        root.requestFullscreen = requestFullscreen;

    const calls = [];
    const document = new FakeElement();
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
            unlock: unlock ?? (() => calls.push("unlock"))
        }
    };

    setupFullscreen(root, document, screen);
    return { root, parts, document, calls };
}

test("full screen hides the browser's controls and asks for landscape", async () => {
    const options = [];
    const { root, parts, calls } = page({ requestFullscreen: o => {
        options.push(o);
        return Promise.resolve();
    } });

    await parts[".fullscreen-open"].fire("click");

    assert.deepEqual(options, [{ navigationUI: "hide" }]);
    assert.deepEqual(calls, ["lock landscape"]);
    assert.ok(root.classes.has("is-fullscreen"));
});

test("when landscape is refused it still goes full screen", async () => {
    const { root, parts } = page({ requestFullscreen: () => Promise.resolve(), lock: () => Promise.reject(new Error("not allowed")) });

    await parts[".fullscreen-open"].fire("click");

    assert.ok(root.classes.has("is-fullscreen"));
});

test("without the Fullscreen API it fills the window", async () => {
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

test("unlocking when nothing was locked is ignored", async () => {
    const { root, document } = page({ unlock: () => {
        throw new Error("not locked");
    } });
    root.classes.add("is-fullscreen");

    await assert.doesNotReject(() => document.fire("fullscreenchange"));
    assert.ok(!root.classes.has("is-fullscreen"));
});
