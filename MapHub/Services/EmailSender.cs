using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MapHub.Services;

public interface IAppEmailSender
{
    /// <summary>Đã cấu hình cách gửi mail chưa (Brevo API HOẶC SMTP).</summary>
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

// Gửi email ưu tiên qua Brevo HTTP API (cổng 443 — KHÔNG bị Render Free chặn như SMTP).
// Nếu không có Brevo thì fallback SMTP (MailKit). Cấu hình bằng env trên Render:
//   Brevo__ApiKey  (bắt buộc để dùng HTTP), Brevo__Sender (email người gửi ĐÃ xác minh trong Brevo)
//   — hoặc SMTP: Smtp__User, Smtp__Pass (chỉ chạy được ở nơi không chặn SMTP).
public class SmtpEmailSender : IAppEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger, IHttpClientFactory httpFactory)
    {
        _config = config;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    private string? BrevoKey => _config["Brevo:ApiKey"];
    private string? SmtpUser => _config["Smtp:User"];
    private string? SmtpPass => _config["Smtp:Pass"];

    private bool HasBrevo => !string.IsNullOrWhiteSpace(BrevoKey);
    private bool HasSmtp => !string.IsNullOrWhiteSpace(SmtpUser) && !string.IsNullOrWhiteSpace(SmtpPass);

    public bool IsConfigured => HasBrevo || HasSmtp;

    // Email người gửi: ưu tiên Brevo:Sender, sau đó Smtp:From, sau đó Smtp:User.
    private string FromEmail => _config["Brevo:Sender"] ?? _config["Smtp:From"] ?? SmtpUser ?? "no-reply@cityscout.app";

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (HasBrevo) { await SendViaBrevoAsync(to, subject, htmlBody); return; }
        if (HasSmtp)  { await SendViaSmtpAsync(to, subject, htmlBody); return; }
        throw new InvalidOperationException("Chưa cấu hình gửi mail (thiếu Brevo__ApiKey hoặc Smtp__User/Smtp__Pass).");
    }

    // ── Brevo HTTP API (khuyên dùng trên Render) ─────────────────────────────
    private async Task SendViaBrevoAsync(string to, string subject, string htmlBody)
    {
        var payload = new
        {
            sender = new { name = "CityScout", email = FromEmail },
            to = new[] { new { email = to } },
            subject,
            htmlContent = htmlBody
        };
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.Add("api-key", BrevoKey);
        req.Headers.Add("accept", "application/json");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            _logger.LogError("Brevo trả về {Status}: {Body}", (int)res.StatusCode, body);
            // Trích message lỗi cho dễ hiểu (Brevo trả JSON {code, message})
            string detail = body;
            try { using var d = JsonDocument.Parse(body); if (d.RootElement.TryGetProperty("message", out var m)) detail = m.GetString() ?? body; }
            catch { }
            throw new Exception($"Brevo lỗi ({(int)res.StatusCode}): {detail}");
        }
        _logger.LogInformation("Đã gửi email tới {To} qua Brevo API", to);
    }

    // ── SMTP dự phòng (MailKit) — chỉ chạy ở nơi không chặn SMTP ──────────────
    private async Task SendViaSmtpAsync(string to, string subject, string htmlBody)
    {
        var host = _config["Smtp:Host"] ?? "smtp.gmail.com";
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("CityScout", FromEmail));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var attempts = int.TryParse(_config["Smtp:Port"], out var p)
            ? new[] { (p, p == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) }
            : new[] { (465, SecureSocketOptions.SslOnConnect), (587, SecureSocketOptions.StartTls) };

        Exception? last = null;
        foreach (var (port, secure) in attempts)
        {
            try
            {
                using var client = new SmtpClient { Timeout = 20_000 };
                await client.ConnectAsync(host, port, secure);
                await client.AuthenticateAsync(SmtpUser, SmtpPass);
                await client.SendAsync(msg);
                await client.DisconnectAsync(true);
                _logger.LogInformation("Đã gửi email tới {To} qua {Host}:{Port}", to, host, port);
                return;
            }
            catch (Exception ex) { last = ex; _logger.LogWarning("SMTP {Host}:{Port} lỗi: {Msg}", host, port, ex.Message); }
        }
        throw last ?? new Exception("Không gửi được email qua SMTP.");
    }
}
