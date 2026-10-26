namespace RunStats.Data;

// The route simplified for the heatmap, as an encoded polyline (the format Google Maps and Leaflet plugins read).
public class RunTrack
{
    public long RunId { get; set; }
    public string EncodedPolyline { get; set; } = "";
    public int PointCount { get; set; }

    // Lets the heatmap load only the tracks that cross the visible part of the map.
    public double MinLatitude { get; set; }
    public double MaxLatitude { get; set; }
    public double MinLongitude { get; set; }
    public double MaxLongitude { get; set; }
}
