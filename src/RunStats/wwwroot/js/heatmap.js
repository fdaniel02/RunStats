// Draws every run's route on a dark map; where routes overlap, the semi-transparent lines add up to bright paths.
// The page puts the tracks into #heatmap-data as JSON; Leaflet (window.L) is loaded before this module.

import { decodePolyline, tiles } from "./map.js";

const LineColor = "#2f81f7";
const MainAreaRadiusKm = 15;

function median(values) {
    const sorted = [...values].sort((a, b) => a - b);
    const middle = Math.floor(sorted.length / 2);
    return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

function distanceKm([lat1, lon1], [lat2, lon2]) {
    const kmPerDegree = 111.32;
    const x = (lon2 - lon1) * kmPerDegree * Math.cos(((lat1 + lat2) / 2) * Math.PI / 180);
    const y = (lat2 - lat1) * kmPerDegree;
    return Math.hypot(x, y);
}

function bounds(tracks) {
    const all = tracks.flat();
    const lats = all.map(p => p[0]);
    const lons = all.map(p => p[1]);
    return [[Math.min(...lats), Math.min(...lons)], [Math.max(...lats), Math.max(...lons)]];
}

// The area most runs are in, as [[south, west], [north, east]]: the tracks whose middle lies within MainAreaRadiusKm
// of the median middle. Fitting the map to all tracks would zoom far out for one race elsewhere.
export function mainArea(tracks) {
    const withPoints = tracks.filter(t => t.length > 0);
    if (withPoints.length === 0)
        return null;
    const middles = withPoints.map(t => {
        const [[south, west], [north, east]] = bounds([t]);
        return [(south + north) / 2, (west + east) / 2];
    });
    const center = [median(middles.map(m => m[0])), median(middles.map(m => m[1]))];
    const near = withPoints.filter((_, i) => distanceKm(middles[i], center) <= MainAreaRadiusKm);
    return bounds(near.length > 0 ? near : withPoints);
}

// Built with textContent: names come from Garmin.
function label(run) {
    const element = document.createElement("div");
    const title = document.createElement("strong");
    title.textContent = run.name || run.type;
    const details = document.createElement("div");
    details.textContent = `${run.date} · ${run.type} · ${run.km} km`;
    element.append(title, details);
    return element;
}

function show(container, runs) {
    const L = window.L;
    // Whole zoom levels: between them, the tiles are scaled and thin seams show where they meet.
    const map = L.map(container, { preferCanvas: true });
    const layer = tiles(container.dataset.cartoKey);
    L.tileLayer(layer.url, layer.options).addTo(map);

    // A wider tolerance than the line itself, so a 2px route is easy to hover and tap.
    const renderer = L.canvas({ tolerance: 6 });
    const tracks = runs.map(run => decodePolyline(run.polyline));
    tracks.forEach((points, i) => {
        const line = L.polyline(points, { renderer, color: LineColor, weight: 2, opacity: 0.35 }).addTo(map);
        line.bindTooltip(label(runs[i]), { sticky: true });
        line.on("mouseover", () => line.setStyle({ weight: 4, opacity: 1 }).bringToFront());
        line.on("mouseout", () => line.setStyle({ weight: 2, opacity: 0.35 }));
    });

    const area = mainArea(tracks);
    if (area)
        map.fitBounds(area, { padding: [24, 24] });
    else
        map.setView([52.1, 5.3], 7);
}

const container = typeof document === "undefined" ? null : document.getElementById("heatmap");
if (container)
    show(container, JSON.parse(document.getElementById("heatmap-data").textContent));
