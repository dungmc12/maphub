using MapHub.Data;
using MapHub.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MapHub.Services;

public interface IProService
{
    // Nâng tài khoản lên Pro theo gói (month/year). Trả về ngày hết hạn mới.
    Task<DateTime> ActivateProAsync(string userId, string planType);

    // Hạ các tài khoản Pro đã hết hạn về Free. Trả về số tài khoản bị hạ.
    Task<int> ExpireOverdueAsync();
}

public class ProService : IProService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly PricingOptions _pricing;

    public ProService(ApplicationDbContext db, UserManager<ApplicationUser> users, IOptions<PricingOptions> pricing)
    {
        _db = db;
        _users = users;
        _pricing = pricing.Value;
    }

    public async Task<DateTime> ActivateProAsync(string userId, string planType)
    {
        var plan = _pricing.For(planType);

        var profile = await _db.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            _db.UserProfiles.Add(profile);
        }

        // Nếu còn hạn Pro thì cộng dồn, không thì tính từ bây giờ
        var start = (profile.Tier == "pro" && profile.ProExpiresAt.HasValue && profile.ProExpiresAt > DateTime.UtcNow)
            ? profile.ProExpiresAt.Value
            : DateTime.UtcNow;

        profile.Tier = "pro";
        profile.ProExpiresAt = start.AddDays(plan.Days);
        profile.MaxPlaces = int.MaxValue;
        profile.MaxPlans = int.MaxValue;
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Đồng bộ role Identity "Pro" (nhiều chỗ trong app check User.IsInRole("Pro"))
        var user = await _users.FindByIdAsync(userId);
        if (user != null && !await _users.IsInRoleAsync(user, "Pro"))
            await _users.AddToRoleAsync(user, "Pro");

        return profile.ProExpiresAt.Value;
    }

    public async Task<int> ExpireOverdueAsync()
    {
        var now = DateTime.UtcNow;
        var overdue = await _db.UserProfiles
            .Where(p => p.Tier == "pro" && p.ProExpiresAt != null && p.ProExpiresAt < now)
            .ToListAsync();

        foreach (var profile in overdue)
        {
            profile.Tier = "free";
            profile.MaxPlaces = 3;
            profile.MaxPlans = 3;
            profile.UpdatedAt = now;

            var user = await _users.FindByIdAsync(profile.UserId);
            if (user != null && await _users.IsInRoleAsync(user, "Pro"))
                await _users.RemoveFromRoleAsync(user, "Pro");
        }

        if (overdue.Count > 0)
            await _db.SaveChangesAsync();

        return overdue.Count;
    }
}
