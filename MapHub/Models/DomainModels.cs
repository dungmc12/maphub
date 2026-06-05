using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MapHub.Models;

public class UserProfile
{
    [Key]
    public string UserId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string Tier { get; set; } = "free";           // free | pro | admin
    public DateTime? ProExpiresAt { get; set; }
    public int MaxPlaces { get; set; } = 3;              // Gói Free: tối đa 3 địa điểm
    public int MaxPlans { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public bool IsPro => Tier == "pro" && (ProExpiresAt == null || ProExpiresAt > DateTime.UtcNow);
    [NotMapped]
    public bool IsAdmin => Tier == "admin";
}

public class Payment
{
    [Key]
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Provider { get; set; } = null!;          // sepay | vnpay | momo...
    public string Status { get; set; } = "pending";        // pending | paid | cancelled
    public string? TransactionId { get; set; }             // mã giao dịch bên SePay/ngân hàng
    public string? Description { get; set; }
    public string? Code { get; set; }                      // nội dung chuyển khoản khớp đơn, vd CSPRO42
    public string PlanType { get; set; } = "month";        // month | year
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AiUsage
{
    [Key]
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime UsageDate { get; set; }
    public int PromptsCount { get; set; }
    public int TokensUsed { get; set; }
}

public class Place
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? About { get; set; }
    public string? Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Phone { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Category { get; set; }
    public string? CreatedByUserId { get; set; }
    public string Visibility { get; set; } = "private";  // private | public
    public string? ShareToken { get; set; }
    public bool IsApproved { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlaceImage> Images { get; set; } = new List<PlaceImage>();
    public ICollection<PlaceAttribute> Attributes { get; set; } = new List<PlaceAttribute>();
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
    public ICollection<PlaceReview> Reviews { get; set; } = new List<PlaceReview>();
}

public class PlaceImage
{
    [Key]
    public int Id { get; set; }
    public int PlaceId { get; set; }
    public string Url { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public bool IsMenu { get; set; }              // Ảnh thực đơn/menu (hiển thị riêng như Google Maps)
    public string? Caption { get; set; }
    public string? UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Place? Place { get; set; }
}

public class PlaceAttribute
{
    [Key]
    public int Id { get; set; }
    public int PlaceId { get; set; }
    public string GroupName { get; set; } = null!;
    public string Label { get; set; } = null!;
    public bool IsAvailable { get; set; } = true;

    public Place? Place { get; set; }
}

public class Tag
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
}

public class PlaceTag
{
    public int PlaceId { get; set; }
    public int TagId { get; set; }
    public Place? Place { get; set; }
    public Tag? Tag { get; set; }
}

public class PlaceReview
{
    [Key]
    public int Id { get; set; }
    public int PlaceId { get; set; }
    public string UserId { get; set; } = null!;
    public byte QualityRating { get; set; }   // Chất lượng tổng thể 1-5
    public byte ServiceRating { get; set; }   // Nhân viên phục vụ 1-5
    public byte? FoodRating { get; set; }     // Đồ ăn/đồ uống 1-5 (cho nhà hàng/cafe)
    public string? FoodReview { get; set; }
    public string? StaffReview { get; set; }
    public string? Content { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Place? Place { get; set; }
}

public class Event
{
    [Key]
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int? PlaceId { get; set; }
    public decimal? Latitude { get; set; }   // vị trí tuỳ chọn của sự kiện (click chỗ trống trên bản đồ)
    public decimal? Longitude { get; set; }
    public string? BannerImageUrl { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public string Status { get; set; } = "upcoming";  // upcoming | ongoing | ended
    public bool IsFeatured { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Place? Place { get; set; }
}

public class FeedPost
{
    [Key]
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Type { get; set; } = "news";  // news | guide | tip
    public int? EventId { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }

    public Event? Event { get; set; }
}

public class UserList
{
    [Key]
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserListItem> Items { get; set; } = new List<UserListItem>();
}

public class UserListItem
{
    public int ListId { get; set; }
    public int PlaceId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public UserList? List { get; set; }
    public Place? Place { get; set; }
}

public class Plan
{
    [Key]
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }   // ghi chú tổng thể kế hoạch
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? ShareToken { get; set; }
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlanItem> Items { get; set; } = new List<PlanItem>();

    [NotMapped]
    public int TotalDays => (StartDate.HasValue && EndDate.HasValue)
        ? Math.Max(1, (int)(EndDate.Value.Date - StartDate.Value.Date).TotalDays + 1) : 1;
}

public class PlanItem
{
    [Key]
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int PlaceId { get; set; }
    public int? DayNumber { get; set; }      // ngày thứ mấy (1, 2, 3...)
    public string? ArrivalTime { get; set; } // giờ đến "08:30" (NVARCHAR)
    public string? LeaveTime { get; set; }   // giờ rời đi "10:00"
    public DateTime? VisitTime { get; set; }
    public int OrderIndex { get; set; }
    public string? Note { get; set; }

    public Plan? Plan { get; set; }
    public Place? Place { get; set; }
}
