using System.Text.Json;
using System.Text.Json.Serialization;
using MapHub.Models;
using Microsoft.Extensions.Options;

namespace MapHub.Services;

// Một giao dịch lấy về từ Casso API
public class CassoApiTx
{
    [JsonPropertyName("tid")]         public string? Tid { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")]      public decimal Amount { get; set; }
    [JsonPropertyName("when")]        public string? When { get; set; }
}

public interface ICassoApiService
{
    bool IsConfigured { get; }
    // Lấy các giao dịch gần nhất (mới → cũ). Trả về rỗng nếu chưa cấu hình / lỗi.
    Task<List<CassoApiTx>> GetRecentTransactionsAsync(int pageSize, CancellationToken ct = default);
    // Buộc Casso đọc ngân hàng ngay (= bấm "Đồng bộ giao dịch ngay"). Casso đẩy webhook nếu có GD mới.
    Task<bool> TriggerSyncAsync(CancellationToken ct = default);
}

public class CassoApiService : ICassoApiService
{
    private readonly HttpClient _http;
    private readonly CassoOptions _opts;
    private readonly ILogger<CassoApiService> _logger;

    public CassoApiService(HttpClient http, IOptions<CassoOptions> opts, ILogger<CassoApiService> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opts.ApiKey);

    public async Task<List<CassoApiTx>> GetRecentTransactionsAsync(int pageSize, CancellationToken ct = default)
    {
        if (!IsConfigured) return new();

        var url = $"{_opts.BaseUrl.TrimEnd('/')}/v2/transactions?pageSize={pageSize}&sort=DESC";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Apikey {_opts.ApiKey}");

            using var resp = await _http.SendAsync(req, ct);
            var payload = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Casso API {Status}: {Body}", (int)resp.StatusCode, payload);
                return new();
            }

            using var doc = JsonDocument.Parse(payload);
            // Cấu trúc Casso v2: { error, data: { records: [...] } }
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("records", out var records) &&
                records.ValueKind == JsonValueKind.Array)
            {
                return records.Deserialize<List<CassoApiTx>>() ?? new();
            }

            _logger.LogWarning("Casso API: không đọc được data.records. Body={Body}", payload);
            return new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Casso API gọi lỗi tới {Url}", url);
            return new();
        }
    }

    public async Task<bool> TriggerSyncAsync(CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(_opts.BankAccountId)) return false;

        var url = $"{_opts.BaseUrl.TrimEnd('/')}/v2/sync";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { bank_acc_id = _opts.BankAccountId }),
                    System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"Apikey {_opts.ApiKey}");

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Casso /v2/sync: đã buộc đồng bộ ngân hàng");
                return true;
            }
            // 429 = gọi sync quá nhanh (giới hạn 1/phút, RPA có thể 1/15 phút) — bình thường, bỏ qua
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Casso /v2/sync {Status}: {Body}", (int)resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Casso /v2/sync lỗi");
            return false;
        }
    }
}
