using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;
using MapHub.Models;

namespace MapHub.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(ApplicationDbContext context, IWebHostEnvironment env, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _env = env;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var pendingCount    = await _context.Places.CountAsync(p => !p.IsApproved);
        var totalPlaces     = await _context.Places.CountAsync();
        var totalEvents     = await _context.Events.CountAsync();
        var totalUsers      = await _context.Users.CountAsync();
        var pendingPayments = await _context.Payments
            .Where(p => p.Status == "confirming")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.UserId, p.Amount, p.PlanType, p.Code, p.TransactionId, p.CreatedAt,
                               Email = _context.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault() })
            .ToListAsync();

        ViewBag.PendingCount    = pendingCount;
        ViewBag.TotalPlaces     = totalPlaces;
        ViewBag.TotalEvents     = totalEvents;
        ViewBag.TotalUsers      = totalUsers;
        ViewBag.PendingPayments = pendingPayments;

        var places = await _context.Places
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync();

        return View(places);
    }

    [HttpGet]
    public async Task<IActionResult> Places(string? filter = null)
    {
        var query = _context.Places
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .AsQueryable();

        query = filter switch
        {
            "pending" => query.Where(p => !p.IsApproved),
            "public"  => query.Where(p => p.Visibility == "public"),
            "private" => query.Where(p => p.Visibility == "private"),
            _         => query
        };

        var places = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        ViewBag.Filter = filter;
        return View(places);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        place.IsApproved = true;
        place.UpdatedAt  = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã duyệt địa điểm: {place.Name}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        _context.Places.Remove(place);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã từ chối và xóa địa điểm.";
        return RedirectToAction(nameof(Index));
    }

    // ── Events ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Events()
    {
        var events = await _context.Events
            .Include(e => e.Place)
            .OrderByDescending(e => e.StartAt)
            .ToListAsync();
        return View(events);
    }

    [HttpGet]
    public async Task<IActionResult> CreateEvent()
    {
        ViewBag.Places = await _context.Places
            .Where(p => p.Visibility == "public" && p.IsApproved)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude })
            .ToListAsync();
        return View(new Event { StartAt = DateTime.Now });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEvent(Event ev, IFormFile? BannerFile)
    {
        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        ev.CreatedByUserId = adminId;

        // Ép Kind=Utc cho cột timestamptz của Postgres (form trả Unspecified -> sẽ lỗi 500)
        ev.StartAt = DateTime.SpecifyKind(ev.StartAt, DateTimeKind.Utc);
        if (ev.EndAt.HasValue) ev.EndAt = DateTime.SpecifyKind(ev.EndAt.Value, DateTimeKind.Utc);

        // File upload takes priority over URL
        if (BannerFile != null && BannerFile.Length > 0)
            ev.BannerImageUrl = await SaveUploadAsync(BannerFile, "events");

        _context.Events.Add(ev);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã tạo sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpGet]
    public async Task<IActionResult> EditEvent(int id)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev == null) return NotFound();
        ViewBag.Places = await _context.Places
            .Where(p => p.Visibility == "public" && p.IsApproved)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude })
            .ToListAsync();
        return View(ev);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEvent(Event ev, IFormFile? BannerFile)
    {
        var existing = await _context.Events.FindAsync(ev.Id);
        if (existing == null) return NotFound();

        existing.Title       = ev.Title;
        existing.Description = ev.Description;
        existing.PlaceId     = ev.PlaceId;
        existing.Latitude    = ev.Latitude;
        existing.Longitude   = ev.Longitude;
        existing.StartAt     = DateTime.SpecifyKind(ev.StartAt, DateTimeKind.Utc);
        existing.EndAt       = ev.EndAt.HasValue ? DateTime.SpecifyKind(ev.EndAt.Value, DateTimeKind.Utc) : null;
        existing.Status      = ev.Status;
        existing.IsFeatured  = ev.IsFeatured;

        // File upload first, then URL if provided, else keep existing
        if (BannerFile != null && BannerFile.Length > 0)
            existing.BannerImageUrl = await SaveUploadAsync(BannerFile, "events");
        else if (!string.IsNullOrWhiteSpace(ev.BannerImageUrl))
            existing.BannerImageUrl = ev.BannerImageUrl;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev != null) { _context.Events.Remove(ev); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFeaturedPlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        place.IsFeatured = !place.IsFeatured;
        place.UpdatedAt  = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Success"] = place.IsFeatured
            ? $"Đã đưa \"{place.Name}\" vào danh sách nổi bật."
            : $"Đã bỏ \"{place.Name}\" khỏi danh sách nổi bật.";
        return RedirectToAction(nameof(Index));
    }

    // ── Feed Posts ───────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> FeedPosts()
    {
        var posts = await _context.FeedPosts
            .OrderByDescending(f => f.IsPinned)
            .ThenByDescending(f => f.PublishedAt)
            .ToListAsync();
        return View(posts);
    }

    [HttpGet]
    public IActionResult CreateFeedPost() => View(new FeedPost());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFeedPost(FeedPost post)
    {
        post.PublishedAt = DateTime.UtcNow;
        _context.FeedPosts.Add(post);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã tạo bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post != null) { _context.FeedPosts.Remove(post); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePinFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post == null) return NotFound();
        post.IsPinned = !post.IsPinned;
        await _context.SaveChangesAsync();
        TempData["Success"] = post.IsPinned ? $"Đã ghim \"{post.Title}\"." : $"Đã bỏ ghim \"{post.Title}\".";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpGet]
    public async Task<IActionResult> EditFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post == null) return NotFound();
        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFeedPost(FeedPost post)
    {
        var existing = await _context.FeedPosts.FindAsync(post.Id);
        if (existing == null) return NotFound();
        existing.Title         = post.Title;
        existing.Summary       = post.Summary;
        existing.CoverImageUrl = post.CoverImageUrl;
        existing.Type          = post.Type;
        existing.IsPinned      = post.IsPinned;
        existing.EventId       = post.EventId;
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    // ── Place management ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> AddPlace()
    {
        ViewBag.AllTags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPlace(Place place, string? imageUrl, IFormFile? imageFile, int[]? tagIds)
    {
        place.IsApproved      = true;
        place.CreatedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        place.CreatedAt       = DateTime.UtcNow;
        place.UpdatedAt       = DateTime.UtcNow;
        _context.Places.Add(place);
        await _context.SaveChangesAsync();

        // Tags
        if (tagIds?.Any() == true)
        {
            var validTagIds = await _context.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            foreach (var tid in validTagIds)
                _context.PlaceTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tid });
        }

        // Ảnh: ưu tiên file upload, sau đó URL
        string? resolvedUrl = null;
        if (imageFile != null && imageFile.Length > 0)
            resolvedUrl = await SaveUploadAsync(imageFile, "places");
        else if (!string.IsNullOrWhiteSpace(imageUrl))
            resolvedUrl = imageUrl;
        if (resolvedUrl != null)
            _context.PlaceImages.Add(new PlaceImage { PlaceId = place.Id, Url = resolvedUrl, IsPrimary = true });

        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm địa điểm: {place.Name}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditPlace(int id)
    {
        var place = await _context.Places
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();
        ViewBag.AllTags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        ViewBag.SelectedTagIds = place.PlaceTags.Select(pt => pt.TagId).ToHashSet();
        return View(place);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlace(int id, string name, string? category, string? about,
        string? address, string? phone, string? websiteUrl, decimal? minPrice, decimal? maxPrice,
        string visibility, string? newImageUrl, IFormFile? imageFile, int[]? tagIds, bool isFeatured = false)
    {
        var place = await _context.Places.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();

        // Cập nhật tags (thêm cái mới tick, bỏ cái bỏ tick)
        var newTagIds = (tagIds ?? Array.Empty<int>()).ToHashSet();
        var currentTags = await _context.PlaceTags.Where(pt => pt.PlaceId == id).ToListAsync();
        _context.PlaceTags.RemoveRange(currentTags.Where(pt => !newTagIds.Contains(pt.TagId)));
        var existingTagIds = currentTags.Select(pt => pt.TagId).ToHashSet();
        foreach (var tid in newTagIds.Where(t => !existingTagIds.Contains(t)))
            _context.PlaceTags.Add(new PlaceTag { PlaceId = id, TagId = tid });

        place.Name       = name;
        place.Category   = category;
        place.About      = about;
        place.Address    = address;
        place.Phone      = phone;
        place.WebsiteUrl = websiteUrl;
        place.MinPrice   = minPrice;
        place.MaxPrice   = maxPrice;
        place.Visibility = visibility;
        place.IsFeatured = isFeatured;
        place.UpdatedAt  = DateTime.UtcNow;

        // Determine new image URL
        string? resolvedUrl = null;
        if (imageFile != null && imageFile.Length > 0)
            resolvedUrl = await SaveUploadAsync(imageFile, "places");
        else if (!string.IsNullOrWhiteSpace(newImageUrl))
            resolvedUrl = newImageUrl;

        if (resolvedUrl != null)
        {
            var primary = place.Images.FirstOrDefault(i => i.IsPrimary);
            if (primary != null) primary.Url = resolvedUrl;
            else _context.PlaceImages.Add(new PlaceImage { PlaceId = place.Id, Url = resolvedUrl, IsPrimary = true });
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật: {place.Name}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place != null) { _context.Places.Remove(place); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa địa điểm.";
        return RedirectToAction(nameof(Index));
    }

    // ── Tag management ───────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Tags()
    {
        var tags = await _context.Tags
            .Include(t => t.PlaceTags)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return View(tags);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Tên tag không được để trống.";
            return RedirectToAction(nameof(Tags));
        }
        if (await _context.Tags.AnyAsync(t => t.Name == name.Trim()))
        {
            TempData["Error"] = "Tag này đã tồn tại.";
            return RedirectToAction(nameof(Tags));
        }
        _context.Tags.Add(new Tag { Name = name.Trim() });
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã tạo tag: {name.Trim()}";
        return RedirectToAction(nameof(Tags));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var tag = await _context.Tags.FindAsync(id);
        if (tag != null) { _context.Tags.Remove(tag); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa tag.";
        return RedirectToAction(nameof(Tags));
    }

    // ── User Management ──────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Users(string? q = null)
    {
        var query = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email!.Contains(q) || u.UserName!.Contains(q));

        var users = await query.OrderBy(u => u.Email).ToListAsync();
        var userRoles = new Dictionary<string, IList<string>>();
        foreach (var u in users)
            userRoles[u.Id] = await _userManager.GetRolesAsync(u);

        var placeCounts = await _context.Places
            .Where(p => p.CreatedByUserId != null)
            .GroupBy(p => p.CreatedByUserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        // Trạng thái Pro/Admin THẬT theo UserProfile (cái user thực sự đang hưởng) — để admin
        // thấy đúng kể cả khi role & profile lệch nhau, và bấm "Bỏ Pro" sửa được.
        var now = DateTime.UtcNow;
        var proUserIds = (await _context.UserProfiles
            .Where(p => p.Tier == "pro" && (p.ProExpiresAt == null || p.ProExpiresAt > now))
            .Select(p => p.UserId).ToListAsync()).ToHashSet();
        var adminUserIds = (await _context.UserProfiles
            .Where(p => p.Tier == "admin")
            .Select(p => p.UserId).ToListAsync()).ToHashSet();

        // Trạng thái khóa tài khoản (Identity lockout) + lý do (lưu ở claim)
        var lockedUserIds = users
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow)
            .Select(u => u.Id).ToHashSet();
        var lockReasons = new Dictionary<string, string>();
        foreach (var u in users.Where(u => lockedUserIds.Contains(u.Id)))
        {
            var reason = (await _userManager.GetClaimsAsync(u)).FirstOrDefault(c => c.Type == "LockReason")?.Value;
            if (reason != null) lockReasons[u.Id] = reason;
        }

        ViewBag.UserRoles   = userRoles;
        ViewBag.PlaceCounts = placeCounts;
        ViewBag.ProUserIds   = proUserIds;
        ViewBag.AdminUserIds = adminUserIds;
        ViewBag.LockedUserIds = lockedUserIds;
        ViewBag.LockReasons   = lockReasons;
        ViewBag.Q           = q;
        return View(users);
    }

    // ── Khóa / mở khóa tài khoản (kèm lý do) ───────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LockUser(string userId, string? reason)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == currentUserId)
        {
            TempData["Error"] = "Không thể khóa tài khoản của chính mình.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        // Lưu lý do qua claim (không cần thêm cột DB)
        foreach (var c in (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "LockReason"))
            await _userManager.RemoveClaimAsync(user, c);
        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim(
            "LockReason", string.IsNullOrWhiteSpace(reason) ? "Vi phạm điều khoản sử dụng." : reason.Trim()));

        // Đẩy user ra khỏi phiên hiện tại (cookie hết hiệu lực trong ~1 phút)
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Success"] = $"Đã khóa tài khoản {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);
        foreach (var c in (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "LockReason"))
            await _userManager.RemoveClaimAsync(user, c);

        TempData["Success"] = $"Đã mở khóa tài khoản {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string userId, string role)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var current = await _userManager.GetRolesAsync(user);
        // Never strip Admin from yourself
        var toRemove = userId == currentUserId
            ? current.Where(r => r != "Admin").ToList()
            : current.ToList();

        await _userManager.RemoveFromRolesAsync(user, toRemove);

        if (role == "Pro")   await _userManager.AddToRoleAsync(user, "Pro");
        if (role == "Admin") await _userManager.AddToRoleAsync(user, "Admin");

        // QUAN TRỌNG: đồng bộ UserProfile.Tier với role.
        // App xác định Pro qua profile.IsPro (Tier=="pro") ở nhiều chỗ — nếu chỉ đổi role
        // mà không đổi Tier thì "Bỏ Pro" không có tác dụng (user vẫn được coi là Pro).
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            _context.UserProfiles.Add(profile);
        }
        if (role == "Admin")
        {
            profile.Tier = "admin";
            profile.ProExpiresAt = null;
            profile.MaxPlaces = int.MaxValue;
            profile.MaxPlans  = int.MaxValue;
        }
        else if (role == "Pro")
        {
            profile.Tier = "pro";
            // Cấp Pro tay: cho 30 ngày nếu chưa có hạn xa hơn
            if (profile.ProExpiresAt == null || profile.ProExpiresAt < DateTime.UtcNow.AddDays(30))
                profile.ProExpiresAt = DateTime.UtcNow.AddDays(30);
            profile.MaxPlaces = int.MaxValue;
            profile.MaxPlans  = int.MaxValue;
        }
        else // "User" = bỏ Pro
        {
            profile.Tier = "free";
            profile.ProExpiresAt = null;
            profile.MaxPlaces = 3;
            profile.MaxPlans  = 3;
        }
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // KHÔNG đổi security stamp (sẽ ép user đăng xuất). Cookie tự làm mới claims trong ~1 phút
        // (SecurityStampValidatorOptions.ValidationInterval) nên role mới có hiệu lực mà không bị logout.

        TempData["Success"] = $"Đã cập nhật vai trò cho {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string userId)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == currentUserId)
        {
            TempData["Error"] = "Không thể xóa tài khoản của chính mình.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            await _userManager.DeleteAsync(user);
            TempData["Success"] = $"Đã xóa tài khoản {user.Email}.";
        }
        return RedirectToAction(nameof(Users));
    }

    // ── Doanh thu ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Revenue()
    {
        var paid = await _context.Payments
            .Where(p => p.Status == "paid")
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new RevenueRow(
                p.Id, p.Amount, p.PlanType, p.Code, p.Provider, p.PaidAt,
                _context.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        ViewBag.Total      = paid.Sum(p => p.Amount);
        ViewBag.Count      = paid.Count;
        ViewBag.MonthTotal = paid.Where(p => p.PaidAt >= monthStart).Sum(p => p.Amount);
        ViewBag.MonthCount = paid.Count(p => p.PaidAt >= monthStart);
        return View(paid);
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    private async Task<string> SaveUploadAsync(IFormFile file, string folder)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext))
            return string.Empty;

        var dir = Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(dir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
        return $"/uploads/{folder}/{fileName}";
    }
}

public record RevenueRow(int Id, decimal Amount, string PlanType, string? Code, string? Provider, DateTime? PaidAt, string? Email);
