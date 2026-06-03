using MapHub.Data;
using MapHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace MapHub.Controllers;

public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly PricingOptions _pricing;
    private readonly SePayOptions _sepay;

    public PaymentsController(
        ApplicationDbContext context,
        IOptions<PricingOptions> pricing,
        IOptions<SePayOptions> sepay)
    {
        _context = context;
        _pricing = pricing.Value;
        _sepay = sepay.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Premium()
    {
        if (User.IsInRole("Admin"))
            return RedirectToAction("Index", "Admin");

        bool isPro = false;
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var profile = await _context.UserProfiles.FindAsync(userId);
                isPro = profile?.IsPro ?? false;
            }
        }
        ViewBag.IsPro = isPro;
        ViewBag.MonthAmount = _pricing.Month.Amount;
        ViewBag.YearAmount = _pricing.Year.Amount;
        return View("Premium");
    }

    // Tạo đơn -> sinh mã nội dung CK duy nhất -> sang trang QR
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string plan)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Challenge();

        plan = string.Equals(plan, "year", StringComparison.OrdinalIgnoreCase) ? "year" : "month";
        var price = _pricing.For(plan);

        var payment = new Payment
        {
            UserId = userId,
            Amount = price.Amount,
            Provider = "sepay",
            Status = "pending",
            PlanType = plan,
            Description = $"CityScout Pro {price.Label}",
            CreatedAt = DateTime.UtcNow
        };
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();        // lấy Id

        payment.Code = $"CSPRO{payment.Id}";       // nội dung CK duy nhất
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Checkout), new { code = payment.Code });
    }

    // Trang hiển thị mã VietQR để quét
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Checkout(string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Code == code && p.UserId == userId);
        if (payment == null) return NotFound();

        var price = _pricing.For(payment.PlanType);

        // Ảnh VietQR miễn phí (nhồi sẵn số tiền + nội dung CK)
        var qrUrl =
            $"https://img.vietqr.io/image/{_sepay.BankCode}-{_sepay.AccountNumber}-compact2.png" +
            $"?amount={(long)payment.Amount}" +
            $"&addInfo={Uri.EscapeDataString(payment.Code!)}" +
            $"&accountName={Uri.EscapeDataString(_sepay.AccountName)}";

        ViewBag.QrUrl = qrUrl;
        ViewBag.BankCode = _sepay.BankCode;
        ViewBag.AccountNumber = _sepay.AccountNumber;
        ViewBag.AccountName = _sepay.AccountName;
        ViewBag.PlanLabel = price.Label;
        return View(payment);
    }

    // Frontend poll trạng thái đơn (đã trả chưa)
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Status(string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .Where(p => p.Code == code && p.UserId == userId)
            .Select(p => new { p.Status })
            .FirstOrDefaultAsync();
        if (payment == null) return NotFound();
        return Json(new { status = payment.Status, paid = payment.Status == "paid" });
    }
}
