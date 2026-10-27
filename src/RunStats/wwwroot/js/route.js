// The page puts the run's track into #route-data as JSON; Leaflet (window.L) is loaded before this module.
import { decodePolyline, tiles } from "./map.js";

const LineColor = "#2f81f7";

export function endpoints(points) {
    return points.length === 0 ? null : { start: points[0], finish: points.at(-1) };
}

function marker(L, point, color, title) {
    return L.circleMarker(point, { radius: 6, weight: 2, color: "#0d1117", fillColor: color, fillOpacity: 1 }).bindTooltip(title);
}

function show(container, track) {
    const L = window.L;
    const points = decodePolyline(track.polyline);
    const map = L.map(container);
    const layer = tiles(container.dataset.cartoKey);
    L.tileLayer(layer.url, layer.options).addTo(map);
    const line = L.polyline(points, { color: LineColor, weight: 3, opacity: 0.9 }).addTo(map);
    const ends = endpoints(points);
    if (ends) {
        marker(L, ends.finish, "#f85149", "Finish").addTo(map);
        marker(L, ends.start, "#3fb950", "Start").addTo(map);
    }
    map.fitBounds(line.getBounds(), { padding: [24, 24] });

    // Where the charts' readout is, set by charts.js.
    const dot = L.circleMarker([0, 0], { radius: 7, weight: 2, color: "#0d1117", fillColor: "#e6edf3", fillOpacity: 1 });
    document.addEventListener("run:position", e => {
        if (e.detail)
            dot.setLatLng(e.detail).addTo(map);
        else
            dot.remove();
    });
}

const container = typeof document === "undefined" ? null : document.getElementById("route");
if (container)
    show(container, JSON.parse(document.getElementById("route-data").textContent));
