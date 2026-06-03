using MapHub.Data;

namespace MapHub.Models;

public class MapPoint
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}
