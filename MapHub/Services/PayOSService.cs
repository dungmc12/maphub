using MapHub.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapHub.Services;

public class PayOSCreateRequest
{
    [JsonPropertyName("orderCode")]   public long OrderCode { get; set; }
    [JsonPropertyName("amount")]      public int Amount { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("items")]       public List<PayOSItem> Items { get; set; } = [];
    [JsonPropertyName("cancelUrl")]   public string CancelUrl { get; set; } = "";
    [JsonPropertyName("returnUrl")]   public string ReturnUrl { get; set; } = "";
    [JsonPropertyName("signature")]   public string Signature { get; set; } = "";
}

public record PayOSItem(
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("price")]    int Price);

public class PayOSCreateResult
{
    [JsonPropertyName("checkoutUrl")] public string CheckoutUrl { get; set; } = "";
    [JsonPropertyName("qrCode")]      public string QrCode { get; set; } = "";
    [JsonPropertyName("paymentLinkId")] public string PaymentLinkId { get; set; } = "";
    [JsonPropertyName("status")]      public string Status { get; set; } = "";
}

public class PayOSWebhookData
{
    [JsonPropertyName("orderCode")]           public long OrderCode { get; set; }
    [JsonPropertyName("amount")]              public int Amount { get; set; }
    [JsonPropertyName("description")]         public string Description { get; set; } = "";
    [JsonPropertyName("reference")]           public string Reference { get; set; } = "";
    [JsonPropertyName("transactionDateTime")] public string TransactionDateTime { get; set; } = "";
    [JsonPropertyName("currency")]            public string Currency { get; set; } = "";
    [JsonPropertyName("paymentLinkId")]       public string PaymentLinkId { get; set; } = "";
    [JsonPropertyName("code")]                public string Code { get; set; } = "";
    [JsonPropertyName("desc")]                public string Desc { get; set; } = "";
    [JsonPropertyName("accountNumber")]       public string AccountNumber { get; set; } = "";
    [JsonPropertyName("counterAccountName")]  public string CounterAccountName { get; set; } = "";
}

public class PayOSWebhookPayload
{
    [JsonPropertyName("code")]      public string Code { get; set; } = "";
    [JsonPropertyName("desc")]      public string Desc { get; set; } = "";
    [JsonPropertyName("success")]   public bool Success { get; set; }
    [JsonPropertyName("data")]      public PayOSWebhookData Data { get; set; } = new();
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

public class PayOSService
{
    private readonly HttpClient _http;
    private readonly PayOSOptions _opts;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PayOSService(HttpClient http, IOptions<PayOSOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
        _http.BaseAddress = new Uri("https://api-merchant.payos.vn");
        _http.DefaultRequestHeaders.Add("x-client-id", _opts.ClientId);
        _http.DefaultRequestHeaders.Add("x-api-key", _opts.ApiKey);
    }

    public async Task<PayOSCreateResult> CreatePaymentLinkAsync(
        long orderCode, int amount, string description,
        string returnUrl, string cancelUrl)
    {
        var sig = Sign(new SortedDictionary<string, string>
        {
            ["amount"]      = amount.ToString(),
            ["cancelUrl"]   = cancelUrl,
            ["description"] = description,
            ["orderCode"]   = orderCode.ToString(),
            ["returnUrl"]   = returnUrl,
        });

        var body = new PayOSCreateRequest
        {
            OrderCode   = orderCode,
            Amount      = amount,
            Description = description,
            Items       = [new PayOSItem(description, 1, amount)],
            CancelUrl   = cancelUrl,
            ReturnUrl   = returnUrl,
            Signature   = sig
        };

        var resp = await _http.PostAsJsonAsync("/v2/payment-requests", body);
        resp.EnsureSuccessStatusCode();

        var envelope = await resp.Content.ReadFromJsonAsync<JsonElement>(_json);
        if (envelope.GetProperty("code").GetString() != "00")
            throw new Exception($"PayOS error: {envelope.GetProperty("desc").GetString()}");

        return envelope.GetProperty("data").Deserialize<PayOSCreateResult>(_json)
               ?? throw new Exception("Empty PayOS response data");
    }

    // Trả về true nếu chữ ký hợp lệ, false nếu không
    public bool VerifyWebhook(PayOSWebhookPayload payload)
    {
        var d = payload.Data;
        var expected = Sign(new SortedDictionary<string, string>
        {
            ["accountNumber"]           = d.AccountNumber,
            ["amount"]                  = d.Amount.ToString(),
            ["code"]                    = d.Code,
            ["counterAccountName"]      = d.CounterAccountName,
            ["currency"]                = d.Currency,
            ["description"]             = d.Description,
            ["orderCode"]               = d.OrderCode.ToString(),
            ["paymentLinkId"]           = d.PaymentLinkId,
            ["reference"]               = d.Reference,
            ["transactionDateTime"]     = d.TransactionDateTime,
        });
        return string.Equals(expected, payload.Signature, StringComparison.OrdinalIgnoreCase);
    }

    private string Sign(SortedDictionary<string, string> data)
    {
        var raw = string.Join("&", data.Select(kv => $"{kv.Key}={kv.Value}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opts.ChecksumKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
