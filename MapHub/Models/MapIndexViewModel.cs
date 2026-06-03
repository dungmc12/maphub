namespace MapHub.Models;

public class MapIndexViewModel
{
    public string UserEmail { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public double DefaultLatitude { get; set; }

    public double DefaultLongitude { get; set; }

    public int DefaultZoom { get; set; }

    public IReadOnlyList<MapPoint> Points { get; set; } = [];

    public bool IsDemoData { get; set; }

    public AddMapPointInputModel NewPoint { get; set; } = new();
}
