using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MapHub.Controllers;

// Casso webhook payload — Casso gửi mảng các giao dịch
public class CassoTransaction
{
    [JsonPropertyName("tid")]         public string? Tid { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")]      public decimal Amount { get; set; }
    [JsonPropertyName("when")]        public string? When { get; set; }
    [JsonPropertyName("bankName")]    public string? BankName { get; set; }
    [JsonPropertyName("subAccId")]    public string? SubAccId { get; set; }
}

public class CassoWebhookPayload
{
    [JsonPropertyName("error")] public int Error { get; set; }
    [JsonPropertyName("data")]  public List<CassoTransaction>? Data { get; set; }
}

[ApiController]
[AllowAnonymous]
[Route("api/casso")]
public class CassoWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProService _proService;
    private readonly SePayOptions _config; // dùng chung ApiKey từ SePay section
    private readonly ILogger<CassoWebhookController> _logger;

    public CassoWebhookController(
        ApplicationDbContext db, IProService proService,
        IOptions<SePayOptions> config, ILogger<CassoWebhookController> logger)
    {
        _db = db; _proService = proService;
        _config = config.Value; _logger = logger;
    }

    // Casso POST đến đây khi có tiền vào tài khoản
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] CassoWebhookPayload? payload)
    {
        // Xác thực API key qua header "apikey"
        var apiKey = Request.Headers["apikey"].ToString();
        if (!string.IsNullOrWhiteSpace(_config.ApiKey) && apiKey != _config.ApiKey)
        {
            _logger.LogWarning("Casso webhook: sai apikey");
            return Ok(new { error = 0 }); // trả 200 để Casso không retry
        }

        if (payload?.Data == null || payload.Error != 0)
            return Ok(new { error = 0 });

        foreach (var tx in payload.Data)
        {
            await ProcessTransaction(tx);
        }

        return Ok(new { error = 0 });
    }

    private async Task ProcessTransaction(CassoTransaction tx)
    {
        var description = $"{tx.Description} {tx.Tid}".ToUpperInvariant();
        var match = Regex.Match(description, @"CSPRO(\d+)");
        if (!match.Success) return;

        var code = "CSPRO" + match.Groups[1].Value;
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Code == code);
        if (payment == null || payment.Status == "paid") return;

        if (tx.Amount < payment.Amount)
        {
            _logger.LogWarning("Casso: số tiền {Got} < cần {Need} cho {Code}", tx.Amount, payment.Amount, code);
            return;
        }

        payment.Status = "paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.TransactionId = tx.Tid;
        await _db.SaveChangesAsync();

        await _proService.ActivateProAsync(payment.UserId, payment.PlanType);
        _logger.LogInformation("Casso: kích hoạt Pro cho đơn {Code}", code);
    }
}
