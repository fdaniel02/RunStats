import { test } from "node:test";
import assert from "node:assert/strict";
import { formatPace, nearestIndex, position, readout } from "../../src/RunStats/wwwroot/js/charts.js";

const kms = [0, 0.5, 1.2, 2, 3.5];

test("the nearest point to a distance is found on either side", () => {
    assert.equal(nearestIndex(kms, 0.2), 0);
    assert.equal(nearestIndex(kms, 0.3), 1);
    assert.equal(nearestIndex(kms, 1.7), 3);
    assert.equal(nearestIndex(kms, 3.5), 4);
});

test("distances beyond the ends give the first and last point", () => {
    assert.equal(nearestIndex(kms, -1), 0);
    assert.equal(nearestIndex(kms, 9), 4);
});

test("a single point is always the nearest", () => {
    assert.equal(nearestIndex([1.5], 0), 0);
});

test("pace reads as minutes and seconds", () => {
    assert.equal(formatPace(300), "5:00");
    assert.equal(formatPace(329.6), "5:30");
    assert.equal(formatPace(65), "1:05");
});

const data = {
    total: 3.5,
    series: [
        { key: "pace", title: "Pace", unit: "/km" },
        { key: "hr", title: "Heart rate", unit: "bpm" },
        { key: "stride", title: "Stride", unit: "m" }
    ],
    km: kms,
    pace: [330, null, 300, 301, 299],
    hr: [120, 150, 160, 170, 171],
    stride: [1.04, 1.1, 1.2, 1.234, 1],
    lat: [51.6, 51.61, null, 51.62, 51.63],
    lon: [5.5, 5.51, null, 5.52, 5.53]
};

test("the readout lists the distance and every chart's value with its unit", () => {
    assert.deepEqual(readout(data, 0), [
        { label: "Distance", value: "0.00 km" },
        { label: "Pace", value: "5:30 /km" },
        { label: "Heart rate", value: "120 bpm" },
        { label: "Stride", value: "1.04 m" }
    ]);
});

test("a value that wasn't recorded reads as a dash", () => {
    assert.deepEqual(readout(data, 1)[1], { label: "Pace", value: "–" });
});

test("stride keeps two decimals", () => {
    assert.equal(readout(data, 4)[3].value, "1.00 m");
});

test("the map position is the point's coordinates, or none without GPS", () => {
    assert.deepEqual(position(data, 3), [51.62, 5.52]);
    assert.equal(position(data, 2), null);
});
