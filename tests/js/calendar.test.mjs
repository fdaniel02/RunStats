import { test } from "node:test";
import assert from "node:assert/strict";

test("the year page's grid gets the full screen buttons", async () => {
    const listened = [];
    const element = name => ({ addEventListener: type => listened.push(`${name} ${type}`) });
    const parts = { ".fullscreen-open": element("open"), ".fullscreen-close": element("close") };
    const root = { querySelector: selector => parts[selector] };
    globalThis.document = { ...element("document"), getElementById: id => (id === "activity" ? root : null) };
    globalThis.screen = {};
    try {
        await import("../../src/RunStats/wwwroot/js/calendar.js");
    } finally {
        delete globalThis.document;
        delete globalThis.screen;
    }

    assert.deepEqual(listened, ["open click", "close click", "document fullscreenchange"]);
});
