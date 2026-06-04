using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MapHub.Controllers;

public class CassoTransaction
{
    [JsonPropertyName("id")]          public long? Id { get; set; }
    [JsonPropertyName("tid")]         public string? Tid { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")]      public decimal Amount { get; set; }
    [JsonPropertyName("when")]        public string? When { get; set; }
    [JsonPropertyName("bankName")]    public string? BankName { get; set; }
    [JsonPropertyName("subAccId")]    public string? SubAccId { get; set; }
}

[ApiController]
[AllowAnonymous]
[Route("api/casso")]
public class CassoWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProService _proService;
    private readonly SePayOptions _config;
    private readonly ILogger<CassoWebhookController> _logger;

    public CassoWebhookController(
        ApplicationDbContext db, IProService proService,
        IOptions<SePayOptions> config, ILogger<CassoWebhookController> logger)
    {
        _db = db; _proService = proService;
        _config = config.Value; _logger = logger;
    }

    // Casso gọi đây khi có tiền vào — data có thể là object hoặc array
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] JsonElement body)
    {
        try
        {
            // Xác thực API key
            var apiKey = Request.Headers["apikey"].ToString();
            if (!string.IsNullOrWhiteSpace(_config.ApiKey) && apiKey != _config.ApiKey)
            {
                _logger.LogWarning("Casso webhook: sai apikey (nhận: {Got})", apiKey);
                return Ok(new { error = 0 });
            }

            // Lấy danh sách giao dịch — Casso v1 gửi object đơn, có bản gửi array
            var transactions = new List<CassoTransaction>();

            if (body.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.ValueKind == JsonValueKind.Array)
                {
                    // Format mới: data là array
                    var list = dataEl.Deserialize<List<CassoTransaction>>();
                    if (list != null) transactions.AddRange(list);
                }
                else if (dataEl.ValueKind == JsonValueKind.Object)
                {
                    // Format cũ (v1): data là object đơn
                    var tx = dataEl.Deserialize<CassoTransaction>();
                    if (tx != null) transactions.Add(tx);
                }
            }

            _logger.LogInformation("Casso webhook: nhận {Count} giao dịch", transactions.Count);

            foreach (var tx in transactions)
            {
                _logger.LogInformation("Casso tx: tid={Tid} amount={Amount} desc={Desc}",
                    tx.Tid, tx.Amount, tx.Description);
                await ProcessTransaction(tx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Casso webhook xử lý lỗi");
        }

        return Ok(new { error = 0 });
    }

    private async Task ProcessTransaction(CassoTransaction tx)
    {
        var haystack = $"{tx.Description} {tx.Tid}".ToUpperInvariant();
        var match = Regex.Match(haystack, @"CSPRO(\d+)");
        if (!match.Success)
        {
            _logger.LogInformation("Casso: không tìm thấy mã CSPRO trong '{Desc}'", tx.Description);
            return;
        }

        var code = "CSPRO" + match.Groups[1].Value;
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Code == code);

        if (payment == null)
        {
            _logger.LogWarning("Casso: không tìm thấy đơn {Code}", code);
            return;
        }
        if (payment.Status == "paid")
        {
            _logger.LogInformation("Casso: đơn {Code} đã paid rồi", code);
            return;
        }
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
        _logger.LogInformation("Casso: kích hoạt Pro thành công cho đơn {Code}", code);
    }
}
