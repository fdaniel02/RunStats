import { test } from "node:test";
import assert from "node:assert/strict";
import { endpoints, followCharts } from "../../src/RunStats/wwwroot/js/route.js";

test("the route starts at its first point and finishes at its last", () => {
    assert.deepEqual(endpoints([[51.6, 5.5], [51.61, 5.52], [51.62, 5.51]]), { start: [51.6, 5.5], finish: [51.62, 5.51] });
});

test("a route of one point starts and finishes there", () => {
    assert.deepEqual(endpoints([[51.6, 5.5]]), { start: [51.6, 5.5], finish: [51.6, 5.5] });
});

test("an empty route has no endpoints", () => {
    assert.equal(endpoints([]), null);
});

test("the map's dot follows the charts' readout and goes away with it", () => {
    const listeners = [];
    const document = { addEventListener: (type, listener) => listeners.push([type, listener]) };
    const calls = [];
    const map = {};
    const dot = {
        setLatLng(position) {
            calls.push(["at", position]);
            return this;
        },
        addTo(target) {
            calls.push(["on", target === map]);
        },
        remove() {
            calls.push(["removed"]);
        }
    };

    followCharts(document, map, dot);
    const [[type, listener]] = listeners;
    listener({ detail: [51.6, 5.5] });
    listener({ detail: null });

    assert.equal(type, "run:position");
    assert.deepEqual(calls, [["at", [51.6, 5.5]], ["on", true], ["removed"]]);
});
