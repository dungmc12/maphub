using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;
using MapHub.Models;
using MapHub.Services;

namespace MapHub.Controllers;

[Authorize]
public class PlansController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAiChatService _aiChat;

    public PlansController(ApplicationDbContext context, IAiChatService aiChat)
    {
        _context = context;
        _aiChat = aiChat;
    }

    public async Task<IActionResult> Index()
    {
        var currentUserId = UserId();
        var plans = await _context.Plans
            .Where(p => p.UserId == currentUserId)
            .Include(p => p.Items).ThenInclude(i => i.Place)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var profile = await _context.UserProfiles.FindAsync(currentUserId);
        ViewBag.MaxPlans = profile?.MaxPlans ?? 3;
        ViewBag.IsPro = User.IsInRole("Pro") || User.IsInRole("Admin");
        return View(plans);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var plan = await LoadPlan(id, UserId());
        if (plan == null) return NotFound();
        return View(plan);
    }

    [AllowAnonymous]
    [HttpGet("Plans/Shared/{token}")]
    public async Task<IActionResult> Shared(string token)
    {
        var plan = await _context.Plans
            .Include(p => p.Items.OrderBy(i => i.DayNumber).ThenBy(i => i.ArrivalTime).ThenBy(i => i.OrderIndex))
                .ThenInclude(i => i.Place).ThenInclude(p => p!.Images.Where(img => img.IsPrimary))
            .Include(p => p.Items).ThenInclude(i => i.Place).ThenInclude(p => p!.Attributes)
            .FirstOrDefaultAsync(p => p.ShareToken == token && p.IsPublic);
        if (plan == null) return NotFound();
        return View("Details", plan);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDescription(int planId, string? description)
    {
        var plan = await _context.Plans.FindAsync(planId);
        if (plan == null || plan.UserId != UserId()) return NotFound();
        plan.Description = description;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = planId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItem(int planId, int itemId,
        string? note, int? dayNumber, string? arrivalTime, string? leaveTime)
    {
        var item = await _context.PlanItems.FindAsync(itemId);
        var plan = item != null ? await _context.Plans.FindAsync(item.PlanId) : null;
        if (item == null || plan?.UserId != UserId() || item.PlanId != planId) return NotFound();

        item.Note        = string.IsNullOrWhiteSpace(note)        ? null : note.Trim();
        item.DayNumber   = dayNumber;
        item.ArrivalTime = string.IsNullOrWhiteSpace(arrivalTime) ? null : arrivalTime.Trim();
        item.LeaveTime   = string.IsNullOrWhiteSpace(leaveTime)   ? null : leaveTime.Trim();

        // Server-side: giờ rời phải sau giờ đến
        if (item.ArrivalTime != null && item.LeaveTime != null
            && string.Compare(item.LeaveTime, item.ArrivalTime, StringComparison.Ordinal) <= 0)
        {
            item.LeaveTime = null; // bỏ qua giờ rời nếu không hợp lệ
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = planId });
    }

    private Task<Plan?> LoadPlan(int id, string userId) =>
        _context.Plans
            .Include(p => p.Items.OrderBy(i => i.DayNumber).ThenBy(i => i.ArrivalTime).ThenBy(i => i.OrderIndex))
                .ThenInclude(i => i.Place).ThenInclude(p => p!.Images.Where(img => img.IsPrimary))
            .Include(p => p.Items).ThenInclude(i => i.Place).ThenInclude(p => p!.Attributes)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, DateTime? startDate, DateTime? endDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Tên kế hoạch không được để trống.";
            return RedirectToAction(nameof(Index));
        }

        if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
        {
            TempData["Error"] = "Ngày về không thể trước ngày khởi hành.";
            return RedirectToAction(nameof(Index));
        }

        var currentUserId = UserId();
        var isPro = User.IsInRole("Pro") || User.IsInRole("Admin");

        if (!isPro)
        {
            var profile = await _context.UserProfiles.FindAsync(currentUserId);
            var planCount = await _context.Plans.CountAsync(p => p.UserId == currentUserId);
            if (planCount >= (profile?.MaxPlans ?? 3))
            {
                TempData["Error"] = "Bạn đã đạt giới hạn kế hoạch. Nâng cấp Pro để tạo thêm!";
                return RedirectToAction(nameof(Index));
            }
        }

        var plan = new Plan
        {
            UserId = currentUserId,
            Title = title,
            StartDate = startDate,
            EndDate = endDate
        };
        _context.Plans.Add(plan);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = plan.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int planId, int placeId, string? note, int orderIndex = 0)
    {
        var plan = await _context.Plans.FindAsync(planId);
        if (plan == null || plan.UserId != UserId()) return NotFound();

        var alreadyIn = await _context.PlanItems.AnyAsync(i => i.PlanId == planId && i.PlaceId == placeId);
        if (!alreadyIn)
        {
            _context.PlanItems.Add(new PlanItem
            {
                PlanId = planId, PlaceId = placeId,
                Note = note, OrderIndex = orderIndex
            });
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Details), new { id = planId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int planId, int itemId)
    {
        var item = await _context.PlanItems.FindAsync(itemId);
        var plan = item != null ? await _context.Plans.FindAsync(item.PlanId) : null;
        if (item == null || plan?.UserId != UserId()) return NotFound();

        _context.PlanItems.Remove(item);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = planId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleShare(int id)
    {
        var plan = await _context.Plans.FindAsync(id);
        if (plan == null || plan.UserId != UserId()) return NotFound();

        if (string.IsNullOrEmpty(plan.ShareToken))
        {
            plan.ShareToken = Guid.NewGuid().ToString("N");
            plan.IsPublic = true;
        }
        else
        {
            plan.ShareToken = null;
            plan.IsPublic = false;
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var plan = await _context.Plans.FindAsync(id);
        if (plan != null && plan.UserId == UserId())
        {
            _context.Plans.Remove(plan);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Lưu kế hoạch do AI tạo
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAiPlan([FromBody] SaveAiPlanRequest request)
    {
        var userId = UserId();
        var isPro = User.IsInRole("Pro") || User.IsInRole("Admin");
        var profile = await _context.UserProfiles.FindAsync(userId);
        var maxPlans = profile?.MaxPlans ?? 3;
        var planCount = await _context.Plans.CountAsync(p => p.UserId == userId);

        if (!isPro && planCount >= maxPlans)
            return Json(new { ok = false, msg = $"Bạn đã đạt giới hạn {maxPlans} kế hoạch. Nâng cấp Pro để tạo không giới hạn!" });

        var start = DateTime.UtcNow.Date;
        var plan = new Plan
        {
            UserId = userId,
            Title = request.Title,
            Description = request.AiText,
            StartDate = start,
            EndDate = start.AddDays(Math.Max(0, request.Days - 1)),
            CreatedAt = DateTime.UtcNow
        };
        _context.Plans.Add(plan);
        await _context.SaveChangesAsync();

        // Tạo PlanItems từ danh sách địa điểm AI đã gợi ý
        if (request.Places?.Any() == true)
        {
            var validIds = request.Places.Select(p => p.PlaceId).Distinct().ToList();
            var existing = (await _context.Places
                .Where(p => validIds.Contains(p.Id))
                .Select(p => p.Id).ToListAsync()).ToHashSet();

            int order = 0;
            foreach (var entry in request.Places.Where(p => existing.Contains(p.PlaceId)))
            {
                _context.Set<PlanItem>().Add(new PlanItem
                {
                    PlanId = plan.Id,
                    PlaceId = entry.PlaceId,
                    DayNumber = entry.DayNumber,
                    ArrivalTime = entry.ArrivalTime,
                    LeaveTime = entry.LeaveTime,
                    Note = entry.Note,
                    OrderIndex = order++
                });
            }
            await _context.SaveChangesAsync();
        }

        return Json(new { ok = true, id = plan.Id });
    }

    // AI tạo kế hoạch (Pro only)
    [HttpPost]
    public async Task<IActionResult> AiGenerate([FromBody] AiPlanRequest request, CancellationToken ct)
    {
        if (!User.IsInRole("Pro") && !User.IsInRole("Admin"))
            return Json(new { error = "Tính năng này dành cho thành viên Pro. Vui lòng nâng cấp tài khoản!" });

        // Lấy địa điểm từ DB phù hợp với destination
        var places = await _context.Places
            .Where(p => p.IsApproved && p.Visibility == "public")
            .OrderByDescending(p => p.IsFeatured)
            .Take(40)
            .Select(p => new { p.Id, p.Name, p.Category, p.Address })
            .ToListAsync(ct);

        var placesCtx = string.Join("\n", places.Select(p =>
            $"- ID={p.Id} | {p.Name} | {p.Category} | {p.Address}"));

        var people = request.People > 0 ? request.People : 2;
        var prompt = $"[DỮ LIỆU ĐỊA ĐIỂM TRONG APP]\n{placesCtx}\n\n" +
                     $"Hãy lập kế hoạch {request.Days} ngày tại/gần {request.Destination} " +
                     $"cho {people} người, sở thích: {request.Interests}.\n" +
                     $"Chia rõ từng ngày, từng buổi (Sáng/Trưa/Chiều/Tối) với giờ cụ thể.\n" +
                     $"Với mỗi địa điểm PHẢI dùng link: [Tên địa điểm](/Map/Details/ID)\n" +
                     $"Cuối kế hoạch, thêm mục **Ước tính chi phí cho {people} người:**\n" +
                     $"- Liệt kê chi phí từng hạng mục (ăn uống, vé, di chuyển...)\n" +
                     $"- Tổng chi phí ước tính (ghi rõ đơn vị VNĐ)\n" +
                     $"Nếu không có địa điểm phù hợp, gợi ý địa điểm gần nhất.";

        var result = await _aiChat.AskAsync(prompt, ct);
        return Json(new { plan = result });
    }

    private string UserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
}

public record AiPlanRequest(string Destination, int Days, string Interests, int People = 2);
public record SaveAiPlanRequest(string Title, string AiText, int Days,
    List<AiPlaceEntry>? Places = null);
public record AiPlaceEntry(int PlaceId, int DayNumber, string? ArrivalTime, string? LeaveTime, string? Note);
