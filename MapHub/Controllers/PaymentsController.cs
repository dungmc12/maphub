using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
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
    private readonly IProService _proService;
    private readonly PayOSService? _payos;
    private readonly ICassoApiService _casso;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext context,
        IOptions<PricingOptions> pricing,
        IOptions<SePayOptions> sepay,
        IProService proService,
        PayOSService? payos,
        ICassoApiService casso,
        IWebHostEnvironment env,
        ILogger<PaymentsController> logger)
    {
        _context = context;
        _pricing = pricing.Value;
        _sepay = sepay.Value;
        _proService = proService;
        _payos = payos;
        _casso = casso;
        _env = env;
        _logger = logger;
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

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string plan)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        plan = string.Equals(plan, "year", StringComparison.OrdinalIgnoreCase) ? "year" : "month";
        var price = _pricing.For(plan);

        var payment = new Payment
        {
            UserId = userId,
            Amount = price.Amount,
            Provider = "payos",
            Status = "pending",
            PlanType = plan,
            Description = $"CityScout Pro {price.Label}",
            CreatedAt = DateTime.UtcNow
        };
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        payment.Code = $"CSPRO{payment.Id}";
        await _context.SaveChangesAsync();

        // Tạo link thanh toán PayOS → redirect trực tiếp
        if (_payos != null)
        {
            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var result = await _payos.CreatePaymentLinkAsync(
                    orderCode:   payment.Id,
                    amount:      (int)payment.Amount,
                    description: payment.Code,
                    returnUrl:   $"{baseUrl}/Payments/Checkout?code={payment.Code}",
                    cancelUrl:   $"{baseUrl}/Payments/Premium"
                );
                return Redirect(result.CheckoutUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayOS tạo link lỗi cho đơn {Id}", payment.Id);
                // Fallback: hiển thị trang Checkout thủ công
            }
        }

        return RedirectToAction("Checkout", new { code = payment.Code });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Checkout(string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Code == code && p.UserId == userId);
        if (payment == null) return NotFound();

        var price = _pricing.For(payment.PlanType);
        var addInfo = Uri.EscapeDataString(payment.Code);
        var accountName = Uri.EscapeDataString(_sepay.AccountName ?? "");
        ViewBag.QrUrl = $"https://img.vietqr.io/image/{_sepay.BankCode}-{_sepay.AccountNumber}-compact2.png" +
                        $"?amount={(long)payment.Amount}&addInfo={addInfo}&accountName={accountName}";
        ViewBag.PlanLabel = price.Label;
        ViewBag.BankCode = _sepay.BankCode;
        ViewBag.AccountNumber = _sepay.AccountNumber;
        ViewBag.AccountName = _sepay.AccountName;
        return View(payment);
    }

    // User xác nhận đã chuyển khoản (kèm mã GD ngân hàng tuỳ chọn)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string code, string? bankRef)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Code == code && p.UserId == userId);
        if (payment == null) return Json(new { ok = false, msg = "Không tìm thấy đơn." });
        if (payment.Status == "paid")
            return Json(new { ok = false, msg = "Đơn đã được duyệt rồi." });

        payment.Status = "confirming";
        payment.TransactionId = bankRef?.Trim();
        await _context.SaveChangesAsync();

        return Json(new { ok = true });
    }

    // Admin duyệt thanh toán thủ công
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var payment = await _context.Payments.FindAsync(id);
        if (payment == null) return NotFound();
        if (payment.Status == "paid") return Json(new { ok = true, msg = "Đã duyệt trước đó." });

        payment.Status = "paid";
        payment.PaidAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _proService.ActivateProAsync(payment.UserId, payment.PlanType);

        return Json(new { ok = true });
    }

    // Admin từ chối thanh toán
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var payment = await _context.Payments.FindAsync(id);
        if (payment == null) return NotFound();

        payment.Status = "cancelled";
        await _context.SaveChangesAsync();

        return Json(new { ok = true });
    }

    // CHỈ Development: giả lập webhook báo đã thanh toán, để test luồng lên Pro trên localhost
    // (Casso/SePay không gọi được vào localhost nên không test webhook thật ở máy được)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DevConfirm(string code)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Code == code && p.UserId == userId);
        if (payment == null) return Json(new { ok = false, msg = "Không tìm thấy đơn." });

        if (payment.Status != "paid")
        {
            payment.Status = "paid";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionId = "DEV-SIMULATED";
            await _context.SaveChangesAsync();
            await _proService.ActivateProAsync(payment.UserId, payment.PlanType);
            _logger.LogInformation("DEV: giả lập thanh toán + lên Pro cho đơn {Code}", code);
        }
        return Json(new { ok = true });
    }

    // Chủ động kiểm tra giao dịch qua Casso API ngay (không chờ webhook).
    // Khớp mã CSPRO trong nội dung CK + đủ số tiền -> lên Pro luôn.
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckNow(string code, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Code == code && p.UserId == userId, ct);
        if (payment == null) return Json(new { ok = false, msg = "Không tìm thấy đơn." });
        if (payment.Status == "paid") return Json(new { ok = true, paid = true });

        if (!_casso.IsConfigured)
            return Json(new { ok = false, paid = false, msg = "Chưa cấu hình Casso API key — vẫn chờ webhook tự động." });

        // Buộc Casso đọc bank ngay (như bấm "Đồng bộ ngay") rồi mới đọc giao dịch
        await _casso.TriggerSyncAsync(ct);
        var txs = await _casso.GetRecentTransactionsAsync(100, ct);
        var wanted = payment.Code!.ToUpperInvariant();
        var match = txs.FirstOrDefault(t =>
            $"{t.Description} {t.Tid}".ToUpperInvariant().Contains(wanted) &&
            t.Amount >= payment.Amount);

        if (match == null)
            return Json(new { ok = true, paid = false, msg = "Chưa thấy giao dịch khớp. Thử lại sau ít phút." });

        payment.Status = "paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.TransactionId = match.Tid;
        await _context.SaveChangesAsync(ct);
        await _proService.ActivateProAsync(payment.UserId, payment.PlanType);
        _logger.LogInformation("Casso API: kiểm tra thủ công khớp + lên Pro cho đơn {Code}", code);

        return Json(new { ok = true, paid = true });
    }

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
