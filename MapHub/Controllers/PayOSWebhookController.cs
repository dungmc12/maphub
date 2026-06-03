using MapHub.Data;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payos")]
public class PayOSWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProService _proService;
    private readonly PayOSService _payos;
    private readonly ILogger<PayOSWebhookController> _logger;

    public PayOSWebhookController(
        ApplicationDbContext db, IProService proService,
        PayOSService payos, ILogger<PayOSWebhookController> logger)
    {
        _db = db; _proService = proService; _payos = payos; _logger = logger;
    }

    // PayOS gọi endpoint này ngay khi nhận được tiền
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] PayOSWebhookPayload? payload)
    {
        if (payload == null)
            return Ok(new { error = 0 });

        // Test webhook từ PayOS dashboard — bỏ qua
        if (payload.Code == "00" && payload.Data.OrderCode == 123)
            return Ok(new { error = 0 });

        if (!_payos.VerifyWebhook(payload))
        {
            _logger.LogWarning("PayOS webhook signature không hợp lệ");
            return Ok(new { error = 0 }); // vẫn trả 200 để PayOS không retry mãi
        }

        // Chỉ xử lý thanh toán thành công
        if (payload.Code != "00" && payload.Data.Code != "00")
            return Ok(new { error = 0 });

        var paymentId = (int)payload.Data.OrderCode;
        var payment = await _db.Payments.FindAsync(paymentId);
        if (payment == null || payment.Status == "paid")
            return Ok(new { error = 0 });

        payment.Status = "paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.TransactionId = payload.Data.Reference;
        await _db.SaveChangesAsync();

        await _proService.ActivateProAsync(payment.UserId, payment.PlanType);
        _logger.LogInformation("PayOS: kích hoạt Pro cho user {User}, đơn {Code}", payment.UserId, payment.Code);

        return Ok(new { error = 0 });
    }
}
