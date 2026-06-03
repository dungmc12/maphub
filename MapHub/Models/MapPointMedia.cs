namespace MapHub.Models;

public class MapPointMedia
{
    public int Id { get; set; }

    public int MapPointId { get; set; }

    public MapPoint? MapPoint { get; set; }

    public string Url { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
