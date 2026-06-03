using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace MapHub.Controllers;

// Nhận webhook từ SePay khi có tiền vào tài khoản ngân hàng -> tự kích hoạt Pro
[ApiController]
[AllowAnonymous]
[Route("api/payments")]
public class SePayWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProService _proService;
    private readonly SePayOptions _sepay;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(
        ApplicationDbContext db,
        IProService proService,
        IOptions<SePayOptions> sepay,
        ILogger<SePayWebhookController> logger)
    {
        _db = db;
        _proService = proService;
        _sepay = sepay.Value;
        _logger = logger;
    }

    [HttpPost("sepay-webhook")]
    public async Task<IActionResult> SePayWebhook([FromBody] SePayWebhookPayload? payload)
    {
        // 1) Xác thực webhook đúng là từ SePay (header: "Authorization: Apikey <key>")
        var auth = Request.Headers["Authorization"].ToString();
        var expected = $"Apikey {_sepay.ApiKey}";
        if (string.IsNullOrWhiteSpace(_sepay.ApiKey) || auth != expected)
        {
            _logger.LogWarning("SePay webhook sai Authorization.");
            return Unauthorized(new { success = false, message = "invalid api key" });
        }

        if (payload == null)
            return BadRequest(new { success = false });

        // Chỉ xử lý giao dịch tiền VÀO
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase))
            return Ok(new { success = true, message = "ignored (not incoming)" });

        // 2) Tìm mã đơn CSPRO<id> trong nội dung chuyển khoản
        var haystack = $"{payload.Content} {payload.Code}".ToUpperInvariant();
        var match = Regex.Match(haystack, "CSPRO(\\d+)");
        if (!match.Success)
        {
            _logger.LogInformation("SePay webhook không thấy mã đơn trong: {Content}", payload.Content);
            return Ok(new { success = true, message = "no order code" });
        }

        var code = "CSPRO" + match.Groups[1].Value;
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Code == code);
        if (payment == null)
            return Ok(new { success = true, message = "order not found" });

        // 3) Idempotent: đơn đã xử lý rồi thì bỏ qua (SePay có thể retry)
        if (payment.Status == "paid")
            return Ok(new { success = true, message = "already paid" });

        // 4) Kiểm tra số tiền chuyển >= số tiền cần
        if (payload.TransferAmount < payment.Amount)
        {
            _logger.LogWarning("SePay: số tiền {Got} < cần {Need} cho đơn {Code}",
                payload.TransferAmount, payment.Amount, code);
            return Ok(new { success = true, message = "insufficient amount" });
        }

        // 5) Đánh dấu đã trả + kích hoạt Pro
        payment.Status = "paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.TransactionId = payload.Id?.ToString() ?? payload.ReferenceCode;
        await _db.SaveChangesAsync();

        await _proService.ActivateProAsync(payment.UserId, payment.PlanType);

        _logger.LogInformation("Đã lên Pro cho user {User} qua đơn {Code}", payment.UserId, code);
        return Ok(new { success = true });
    }
}

// Payload SePay gửi (camelCase JSON tự map vào PascalCase nhờ ApiController)
public class SePayWebhookPayload
{
    public long? Id { get; set; }
    public string? Gateway { get; set; }
    public string? TransactionDate { get; set; }
    public string? AccountNumber { get; set; }
    public string? Code { get; set; }
    public string? Content { get; set; }
    public string? TransferType { get; set; }   // "in" | "out"
    public decimal TransferAmount { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Description { get; set; }
}
