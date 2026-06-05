using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;
using MapHub.Models;
using MapHub.Services;

namespace MapHub.Controllers;

public class MapController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;

    public MapController(ApplicationDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public async Task<IActionResult> Index(string? cat = null)
    {
        ViewBag.AllTags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        // SEO: mỗi danh mục có tiêu đề + mô tả + H1 + canonical riêng (tránh trùng nội dung)
        var (label, desc) = (cat ?? "").ToLower() switch
        {
            "restaurant"    => ("Nhà hàng", "Khám phá nhà hàng ngon tại Hà Nội trên bản đồ CityScout — lọc theo ngân sách, giờ mở cửa và thời tiết."),
            "cafe"          => ("Quán cà phê", "Tìm quán cà phê đẹp ở Hà Nội — view, không gian làm việc, cà phê trứng — trên bản đồ CityScout."),
            "entertainment" => ("Địa điểm vui chơi", "Địa điểm vui chơi giải trí tại Hà Nội: khu vui chơi, giải trí, trải nghiệm — bản đồ CityScout."),
            "culture"       => ("Địa điểm văn hóa", "Di tích, bảo tàng, địa điểm văn hóa lịch sử tại Hà Nội trên bản đồ CityScout."),
            "temple"        => ("Địa điểm tâm linh", "Chùa, đền, địa điểm tâm linh tại Hà Nội — bản đồ CityScout."),
            "nature"        => ("Địa điểm thiên nhiên", "Hồ, công viên, không gian xanh tại Hà Nội — bản đồ CityScout."),
            "education"     => ("Địa điểm giáo dục", "Trường học, thư viện, địa điểm học tập tại Hà Nội — bản đồ CityScout."),
            "hotel"         => ("Khách sạn & lưu trú", "Khách sạn, homestay, nơi lưu trú tại Hà Nội trên bản đồ CityScout."),
            _               => ("Bản đồ địa điểm", "Khám phá địa điểm ăn uống, cà phê, vui chơi, văn hóa tại Hà Nội trên bản đồ CityScout. Lọc theo ngân sách, giờ mở cửa, thời tiết.")
        };

        ViewData["Title"] = $"{label} tại Hà Nội";
        ViewData["MetaDescription"] = desc;
        ViewBag.H1 = $"{label} tại Hà Nội — CityScout";
        ViewData["Canonical"] = string.IsNullOrWhiteSpace(cat)
            ? $"{Request.Scheme}://{Request.Host}/Map"
            : $"{Request.Scheme}://{Request.Host}/Map?cat={cat}";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetPlaces(string? category = null, string? q = null, decimal? maxPrice = null)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var query = _context.Places
            .Where(p => (p.Visibility == "public" && p.IsApproved)
                     || (p.Visibility == "private" && p.CreatedByUserId == currentUserId))
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .Include(p => p.Reviews)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category != null && p.Category.ToLower() == category.ToLower());

        if (!string.IsNullOrWhiteSpace(q))
        {
            // So khớp không phân biệt hoa/thường (PostgreSQL Contains mặc định phân biệt hoa/thường)
            var ql = q.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(ql)
                                  || (p.Address != null && p.Address.ToLower().Contains(ql)));
        }

        // Lọc theo ngân sách: giá khởi điểm trong tầm tiền (địa điểm miễn phí/chưa rõ giá vẫn hiện)
        if (maxPrice.HasValue)
            query = query.Where(p => p.MinPrice == null || p.MinPrice <= maxPrice.Value);

        var rows = await query.Select(p => new
        {
            p.Id, p.Name, p.Description, p.Latitude, p.Longitude,
            p.Address, p.Category, p.Phone, p.MinPrice, p.MaxPrice,
            Rating = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.QualityRating), 1) : 0.0,
            ReviewCount = p.Reviews.Count,
            PrimaryImage = p.Images.Any()
                ? p.Images.First().Url
                : "https://picsum.photos/seed/default-place/400/300"
        }).ToListAsync();

        // Đánh dấu địa điểm đã lưu của user hiện tại
        var savedIds = currentUserId == null ? new HashSet<int>()
            : (await _context.UserListItems
                .Where(i => _context.UserLists.Any(l => l.Id == i.ListId && l.UserId == currentUserId))
                .Select(i => i.PlaceId).ToListAsync()).ToHashSet();

        var places = rows.Select(p => new
        {
            p.Id, p.Name, p.Description, p.Latitude, p.Longitude,
            p.Address, p.Category, p.Phone, p.MinPrice, p.MaxPrice,
            p.Rating, p.ReviewCount, p.PrimaryImage,
            IsSaved = savedIds.Contains(p.Id)
        });

        return Json(places);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var place = await _context.Places
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Attributes)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (place == null) return NotFound();

        var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        ViewBag.IsSaved = uid != null && await _context.UserListItems
            .AnyAsync(i => i.PlaceId == id && _context.UserLists.Any(l => l.Id == i.ListId && l.UserId == uid));

        // Tên + avatar người đánh giá (kiểu Google: ưu tiên tên hiển thị, không có thì lấy phần trước @ của email)
        var reviewerIds = place.Reviews.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.UserProfiles
            .Where(p => reviewerIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl })
            .ToListAsync();
        var emails = await _context.Users
            .Where(u => reviewerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync();

        var authors = new Dictionary<string, (string Name, string? Avatar)>();
        foreach (var rid in reviewerIds)
        {
            var prof = profiles.FirstOrDefault(p => p.UserId == rid);
            var email = emails.FirstOrDefault(e => e.Id == rid)?.Email;
            var name = !string.IsNullOrWhiteSpace(prof?.DisplayName)
                ? prof!.DisplayName!
                : (!string.IsNullOrWhiteSpace(email) ? email!.Split('@')[0] : "Người dùng");
            authors[rid] = (name, prof?.AvatarUrl);
        }
        ViewBag.ReviewAuthors = authors;
        return View(place);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddPlace([FromBody] PlaceDto dto)
    {
        if (dto == null) return BadRequest("Dữ liệu không hợp lệ.");

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var isAdmin = User.IsInRole("Admin");
        var isPro = User.IsInRole("Pro");

        // Kiểm tra giới hạn địa điểm cho người dùng Free
        if (!isAdmin && !isPro)
        {
            var profile = await _context.UserProfiles.FindAsync(currentUserId);
            var userPlaceCount = await _context.Places.CountAsync(p => p.CreatedByUserId == currentUserId);
            var maxPlaces = profile?.MaxPlaces ?? 3;
            if (userPlaceCount >= maxPlaces)
                return BadRequest(new { error = "limit", message = $"Bạn đã đạt giới hạn {maxPlaces} địa điểm. Nâng cấp Pro để thêm không giới hạn!" });
        }

        var place = new Place
        {
            Name = dto.Name,
            Category = dto.Category,
            About = dto.About,
            Address = dto.Address,
            Phone = dto.Phone,
            WebsiteUrl = dto.WebsiteUrl,
            MinPrice = dto.MinPrice,
            MaxPrice = dto.MaxPrice,
            Visibility = dto.Visibility,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            CreatedByUserId = currentUserId,
            // Công khai hiển thị ngay, không cần Admin duyệt (theo yêu cầu)
            IsApproved = true
        };

        _context.Places.Add(place);
        await _context.SaveChangesAsync();

        // Gán tags đã chọn
        if (dto.TagIds?.Any() == true)
        {
            var validTagIds = (await _context.Tags.Where(t => dto.TagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync());
            foreach (var tid in validTagIds)
                _context.PlaceTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tid });
            await _context.SaveChangesAsync();
        }

        // Lưu ảnh đầu tiên nếu có URL
        if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
        {
            _context.PlaceImages.Add(new PlaceImage
            {
                PlaceId = place.Id,
                Url = dto.ImageUrl,
                IsPrimary = true,
                UploadedByUserId = currentUserId
            });
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, id = place.Id });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UploadImage(int placeId, IFormFile image)
    {
        var place = await _context.Places.FindAsync(placeId);
        if (place == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (place.CreatedByUserId != currentUserId && !User.IsInRole("Admin"))
            return Forbid();

        // Lưu thành base64 data URL (bền với Render redeploy)
        var dataUrl = await ImageHelper.ToDataUrlAsync(image);
        if (dataUrl == null) return BadRequest("Định dạng/kích thước ảnh không hợp lệ (≤5MB).");

        var hasPrimary = await _context.PlaceImages.AnyAsync(i => i.PlaceId == placeId && i.IsPrimary);
        _context.PlaceImages.Add(new PlaceImage
        {
            PlaceId = placeId,
            Url = dataUrl,
            IsPrimary = !hasPrimary,
            UploadedByUserId = currentUserId
        });
        await _context.SaveChangesAsync();

        return Ok(new { url = dataUrl });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReview(int placeId, byte qualityRating, byte serviceRating,
        byte? foodRating, string? content, string? foodReview, string? staffReview, IFormFile? photo)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;

        // Lưu ảnh review thành base64 data URL (tồn tại vĩnh viễn, không mất khi Render redeploy)
        string? photoUrl = await ImageHelper.ToDataUrlAsync(photo);

        _context.PlaceReviews.Add(new PlaceReview
        {
            PlaceId = placeId,
            UserId = currentUserId,
            QualityRating = qualityRating,
            ServiceRating = serviceRating,
            FoodRating = foodRating,
            Content = content,
            FoodReview = foodReview,
            StaffReview = staffReview,
            PhotoUrl = photoUrl
        });
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = placeId });
    }

    // Xóa đánh giá (chủ đánh giá hoặc Admin) — gọi AJAX, không cần load lại trang
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var review = await _context.PlaceReviews.FindAsync(id);
        if (review == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (review.UserId != currentUserId && !User.IsInRole("Admin"))
            return Forbid();

        _context.PlaceReviews.Remove(review);
        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // Sửa đánh giá (chỉ chủ đánh giá hoặc Admin)
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditReview(int id, byte qualityRating, byte serviceRating,
        byte? foodRating, string? content, string? foodReview, string? staffReview, IFormFile? photo)
    {
        var review = await _context.PlaceReviews.FindAsync(id);
        if (review == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (review.UserId != currentUserId && !User.IsInRole("Admin"))
            return Forbid();

        review.QualityRating = qualityRating;
        review.ServiceRating = serviceRating;
        review.FoodRating    = foodRating;
        review.Content       = content;
        review.FoodReview    = foodReview;
        review.StaffReview   = staffReview;

        // Chỉ thay ảnh khi người dùng tải ảnh mới (không chọn thì giữ ảnh cũ)
        var newPhoto = await ImageHelper.ToDataUrlAsync(photo);
        if (newPhoto != null) review.PhotoUrl = newPhoto;

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = review.PlaceId });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyPlaces()
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var places = await _context.Places
            .Where(p => p.CreatedByUserId == currentUserId)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        // Địa điểm đã LƯU (yêu thích) — của bất kỳ ai, lưu qua UserList/UserListItem
        var savedIds = await _context.UserListItems
            .Where(i => _context.UserLists.Any(l => l.Id == i.ListId && l.UserId == currentUserId))
            .OrderByDescending(i => i.AddedAt)
            .Select(i => i.PlaceId)
            .ToListAsync();
        var savedLoaded = await _context.Places
            .Where(p => savedIds.Contains(p.Id))
            .Include(p => p.Images.Where(im => im.IsPrimary))
            .ToListAsync();
        var saved = savedIds
            .Select(sid => savedLoaded.FirstOrDefault(p => p.Id == sid))
            .Where(p => p != null)
            .ToList();

        var profile = await _context.UserProfiles.FindAsync(currentUserId);
        ViewBag.MaxPlaces  = profile?.MaxPlaces ?? 3;
        ViewBag.PlaceCount = places.Count;
        ViewBag.IsPro      = User.IsInRole("Pro") || User.IsInRole("Admin");
        ViewBag.SavedPlaces = saved;
        return View(places);
    }

    // Lấy/ tạo danh sách "Yêu thích" mặc định của user
    private async Task<UserList> GetOrCreateFavListAsync(string userId)
    {
        var list = await _context.UserLists.FirstOrDefaultAsync(l => l.UserId == userId && l.Name == "Yêu thích");
        if (list == null)
        {
            list = new UserList { UserId = userId, Name = "Yêu thích" };
            _context.UserLists.Add(list);
            await _context.SaveChangesAsync();
        }
        return list;
    }

    // Lưu / bỏ lưu địa điểm (toggle) — AJAX
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSave(int placeId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        if (!await _context.Places.AnyAsync(p => p.Id == placeId))
            return NotFound();

        var list = await GetOrCreateFavListAsync(userId);
        var item = await _context.UserListItems.FirstOrDefaultAsync(i => i.ListId == list.Id && i.PlaceId == placeId);
        bool saved;
        if (item != null) { _context.UserListItems.Remove(item); saved = false; }
        else { _context.UserListItems.Add(new UserListItem { ListId = list.Id, PlaceId = placeId }); saved = true; }
        await _context.SaveChangesAsync();
        return Json(new { ok = true, saved });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMyPlace(int id)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var place = await _context.Places.FindAsync(id);
        if (place == null || place.CreatedByUserId != currentUserId)
            return Forbid();

        _context.Places.Remove(place);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa địa điểm \"{place.Name}\".";
        return RedirectToAction(nameof(MyPlaces));
    }

    // Sửa địa điểm cá nhân của chính mình
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> EditMyPlace(int id)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var place = await _context.Places.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();
        if (place.CreatedByUserId != currentUserId && !User.IsInRole("Admin")) return Forbid();
        return View(place);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMyPlace(int id, string name, string? category, string? about,
        string? address, string? phone, decimal? minPrice, decimal? maxPrice, string visibility,
        decimal latitude, decimal longitude, string? newImageUrl, IFormFile? imageFile)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var place = await _context.Places.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();
        if (place.CreatedByUserId != currentUserId && !User.IsInRole("Admin")) return Forbid();

        place.Name       = name;
        place.Category   = category;
        place.About      = about;
        place.Address    = address;
        place.Phone      = phone;
        place.MinPrice   = minPrice;
        place.MaxPrice   = maxPrice;
        place.Visibility = visibility;
        if (latitude != 0 && longitude != 0) { place.Latitude = latitude; place.Longitude = longitude; }
        place.UpdatedAt  = DateTime.UtcNow;

        string? resolvedUrl = await ImageHelper.ToDataUrlAsync(imageFile);
        if (resolvedUrl == null && !string.IsNullOrWhiteSpace(newImageUrl))
            resolvedUrl = newImageUrl;
        if (resolvedUrl != null)
        {
            var primary = place.Images.FirstOrDefault(i => i.IsPrimary);
            if (primary != null) primary.Url = resolvedUrl;
            else _context.PlaceImages.Add(new PlaceImage { PlaceId = place.Id, Url = resolvedUrl, IsPrimary = true, UploadedByUserId = currentUserId });
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật \"{place.Name}\".";
        return RedirectToAction(nameof(MyPlaces));
    }
}

public class PlaceDto
{
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? About { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? WebsiteUrl { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Visibility { get; set; } = "private";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? ImageUrl { get; set; }
    public List<int>? TagIds { get; set; }
}
