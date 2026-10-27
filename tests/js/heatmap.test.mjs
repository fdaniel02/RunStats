import { test } from "node:test";
import assert from "node:assert/strict";
import { decodePolyline, mainArea, tiles } from "../../src/RunStats/wwwroot/js/heatmap.js";

test("decodes Google's example polyline", () => {
    assert.deepEqual(decodePolyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@"), [[38.5, -120.2], [40.7, -120.95], [43.252, -126.453]]);
});

test("decodes steps of one hundred-thousandth in both directions", () => {
    assert.deepEqual(decodePolyline("oos~Hoaz\\@AA@"), [[52.37, 4.89], [52.36999, 4.89001], [52.37, 4.89]]);
});

test("an empty polyline has no points", () => {
    assert.deepEqual(decodePolyline(""), []);
});

test("the main area leaves out a run far from the others", () => {
    const home = [[[51.60, 5.50], [51.62, 5.52]], [[51.61, 5.49], [51.63, 5.53]], [[51.59, 5.51], [51.60, 5.54]]];
    const far = [[52.37, 4.89], [52.38, 4.90]];

    assert.deepEqual(mainArea([...home, far]), [[51.59, 5.49], [51.63, 5.54]]);
});

test("the main area includes everything when the runs are spread out", () => {
    const tracks = [[[51.0, 5.0], [51.01, 5.01]], [[53.0, 6.0], [53.01, 6.01]]];

    assert.deepEqual(mainArea(tracks), [[51.0, 5.0], [53.01, 6.01]]);
});

test("the main area of one run is that run", () => {
    assert.deepEqual(mainArea([[[51.6, 5.5], [51.7, 5.4]]]), [[51.6, 5.4], [51.7, 5.5]]);
});

test("no tracks or only empty ones have no main area", () => {
    assert.equal(mainArea([]), null);
    assert.equal(mainArea([[], []]), null);
});

test("tracks without points are ignored for the main area", () => {
    assert.deepEqual(mainArea([[], [[51.6, 5.5], [51.61, 5.51]]]), [[51.6, 5.5], [51.61, 5.51]]);
});

test("with a CARTO key the map is Dark Matter, with the key in the address", () => {
    const layer = tiles("abc/123");

    assert.equal(layer.url, "https://basemaps.cartocdn.com/rastertiles/dark_all/{z}/{x}/{y}.png?key=abc%2F123");
    assert.match(layer.options.attribution, /OpenStreetMap.*CARTO/);
    assert.equal(layer.options.className, undefined);
});

test("without a key the map is OpenStreetMap, darkened", () => {
    for (const key of [undefined, ""]) {
        const layer = tiles(key);
        assert.equal(layer.url, "https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        assert.equal(layer.options.className, "dark-tiles");
        assert.doesNotMatch(layer.options.attribution, /CARTO/);
    }
});
