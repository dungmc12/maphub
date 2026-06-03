using MapHub.Data;

namespace MapHub.Models;

public class MapPointFavorite
{
    public int Id { get; set; }

    public int MapPointId { get; set; }

    public MapPoint? MapPoint { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
