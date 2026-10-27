// Google's encoded polyline format with 5 decimals, the format RunTracks stores.
export function decodePolyline(encoded) {
    const points = [];
    let index = 0, latitude = 0, longitude = 0;
    const next = () => {
        let result = 0, shift = 0, byte;
        do {
            byte = encoded.codePointAt(index++) - 63;
            result |= (byte & 0x1f) << shift;
            shift += 5;
        } while (byte >= 0x20);
        return result & 1 ? ~(result >> 1) : result >> 1;
    };
    while (index < encoded.length) {
        latitude += next();
        longitude += next();
        points.push([latitude / 1e5, longitude / 1e5]);
    }
    return points;
}

const Osm = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

// CARTO's Dark Matter needs a key; without one, OpenStreetMap's tiles are darkened with CSS (.dark-tiles in app.css).
export function tiles(cartoKey) {
    return cartoKey
        ? {
            url: `https://basemaps.cartocdn.com/rastertiles/dark_all/{z}/{x}/{y}.png?key=${encodeURIComponent(cartoKey)}`,
            options: { maxZoom: 20, attribution: `${Osm} &copy; <a href="https://carto.com/attributions">CARTO</a>` }
        }
        : {
            url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            options: { maxZoom: 19, className: "dark-tiles", attribution: Osm }
        };
}
