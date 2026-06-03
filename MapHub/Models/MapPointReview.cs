using MapHub.Data;

namespace MapHub.Models;

public class MapPointReview
{
    public int Id { get; set; }

    public int MapPointId { get; set; }

    public MapPoint? MapPoint { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public int Rating { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
