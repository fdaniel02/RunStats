import { test } from "node:test";
import assert from "node:assert/strict";
import { decodePolyline, tiles } from "../../src/RunStats/wwwroot/js/map.js";

test("decodes Google's example polyline", () => {
    assert.deepEqual(decodePolyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@"), [[38.5, -120.2], [40.7, -120.95], [43.252, -126.453]]);
});

test("decodes steps of one hundred-thousandth in both directions", () => {
    assert.deepEqual(decodePolyline("oos~Hoaz\\@AA@"), [[52.37, 4.89], [52.36999, 4.89001], [52.37, 4.89]]);
});

test("an empty polyline has no points", () => {
    assert.deepEqual(decodePolyline(""), []);
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
