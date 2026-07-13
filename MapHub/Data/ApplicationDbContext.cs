using MapHub.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Data;

// IDataProtectionKeyContext: lưu DataProtection keys vào DB để cookie đăng nhập KHÔNG bị
// vô hiệu mỗi lần Render restart/redeploy (key tạo mới sẽ làm hỏng mọi cookie cũ → đăng xuất).
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();
    
    public DbSet<Place> Places => Set<Place>();
    public DbSet<PlaceImage> PlaceImages => Set<PlaceImage>();
    public DbSet<PlaceAttribute> PlaceAttributes => Set<PlaceAttribute>();
    
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();
    
    public DbSet<Event> Events => Set<Event>();
    public DbSet<FeedPost> FeedPosts => Set<FeedPost>();
    
    public DbSet<UserList> UserLists => Set<UserList>();
    public DbSet<UserListItem> UserListItems => Set<UserListItem>();
    
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanItem> PlanItems => Set<PlanItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Composite keys
        builder.Entity<PlaceTag>().HasKey(pt => new { pt.PlaceId, pt.TagId });
        builder.Entity<UserListItem>().HasKey(ul => new { ul.ListId, ul.PlaceId });

        // AI Usage constraints
        builder.Entity<AiUsage>().HasIndex(a => new { a.UserId, a.UsageDate }).IsUnique();

        // Specific precision for Geography fallback if needed, EF handles it but decimal precision for Lat/Lng
        builder.Entity<Place>()
            .Property(p => p.Latitude)
            .HasPrecision(9, 6);
            
        builder.Entity<Place>()
            .Property(p => p.Longitude)
            .HasPrecision(9, 6);

        builder.Entity<Place>()
            .Property(p => p.MinPrice)
            .HasPrecision(12, 2);

        builder.Entity<Place>()
            .Property(p => p.MaxPrice)
            .HasPrecision(12, 2);

        builder.Entity<Payment>()
            .Property(p => p.Amount)
            .HasPrecision(18, 2);

        // Plan share token index
        builder.Entity<Plan>()
            .HasIndex(p => p.ShareToken)
            .IsUnique()
            .HasFilter("\"ShareToken\" IS NOT NULL");

        // Event → Place: no cascade to avoid multiple path issues
        builder.Entity<Event>()
            .HasOne(e => e.Place)
            .WithMany()
            .HasForeignKey(e => e.PlaceId)
            .OnDelete(DeleteBehavior.SetNull);

        // FeedPost → Event
        builder.Entity<FeedPost>()
            .HasOne(f => f.Event)
            .WithMany()
            .HasForeignKey(f => f.EventId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
