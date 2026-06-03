using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;
using MapHub.Models;

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

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetPlaces(string? category = null, string? q = null)
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
            query = query.Where(p => p.Name.Contains(q) || (p.Address != null && p.Address.Contains(q)));

        var places = await query.Select(p => new
        {
            p.Id, p.Name, p.Description, p.Latitude, p.Longitude,
            p.Address, p.Category, p.Phone,
            Rating = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.QualityRating), 1) : 0.0,
            ReviewCount = p.Reviews.Count,
            PrimaryImage = p.Images.Any()
                ? p.Images.First().Url
                : "https://picsum.photos/seed/default-place/400/300"
        }).ToListAsync();

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
            IsApproved = isAdmin || dto.Visibility == "private"
        };

        _context.Places.Add(place);
        await _context.SaveChangesAsync();

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

        if (image == null || image.Length == 0)
            return BadRequest("Không có ảnh.");

        // Kiểm tra định dạng
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest("Định dạng ảnh không hỗ trợ.");

        var folder = Path.Combine(_env.WebRootPath, "uploads", "places", placeId.ToString());
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(folder, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await image.CopyToAsync(stream);

        var hasPrimary = await _context.PlaceImages.AnyAsync(i => i.PlaceId == placeId && i.IsPrimary);
        _context.PlaceImages.Add(new PlaceImage
        {
            PlaceId = placeId,
            Url = $"/uploads/places/{placeId}/{fileName}",
            IsPrimary = !hasPrimary,
            UploadedByUserId = currentUserId
        });
        await _context.SaveChangesAsync();

        return Ok(new { url = $"/uploads/places/{placeId}/{fileName}" });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReview(int placeId, byte qualityRating, byte serviceRating,
        byte? foodRating, string? content, string? foodReview, string? staffReview, IFormFile? photo)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;

        string? photoUrl = null;
        if (photo != null && photo.Length > 0 && photo.Length <= 5 * 1024 * 1024)
        {
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext))
            {
                var dir = Path.Combine(_env.WebRootPath, "uploads", "reviews");
                Directory.CreateDirectory(dir);
                var fileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(dir, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await photo.CopyToAsync(stream);
                photoUrl = $"/uploads/reviews/{fileName}";
            }
        }

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

        var profile = await _context.UserProfiles.FindAsync(currentUserId);
        ViewBag.MaxPlaces  = profile?.MaxPlaces ?? 3;
        ViewBag.PlaceCount = places.Count;
        ViewBag.IsPro      = User.IsInRole("Pro") || User.IsInRole("Admin");
        return View(places);
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
}
