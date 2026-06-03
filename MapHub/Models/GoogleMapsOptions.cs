namespace MapHub.Models;

public class GoogleMapsOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public double DefaultLatitude { get; set; } = 10.7769;

    public double DefaultLongitude { get; set; } = 106.7009;

    public int DefaultZoom { get; set; } = 12;
}
