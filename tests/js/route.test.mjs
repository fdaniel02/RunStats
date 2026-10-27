import { test } from "node:test";
import assert from "node:assert/strict";
import { endpoints } from "../../src/RunStats/wwwroot/js/route.js";

test("the route starts at its first point and finishes at its last", () => {
    assert.deepEqual(endpoints([[51.6, 5.5], [51.61, 5.52], [51.62, 5.51]]), { start: [51.6, 5.5], finish: [51.62, 5.51] });
});

test("a route of one point starts and finishes there", () => {
    assert.deepEqual(endpoints([[51.6, 5.5]]), { start: [51.6, 5.5], finish: [51.6, 5.5] });
});

test("an empty route has no endpoints", () => {
    assert.equal(endpoints([]), null);
});
