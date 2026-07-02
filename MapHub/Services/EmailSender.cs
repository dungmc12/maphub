using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MapHub.Services;

public interface IAppEmailSender
{
    /// <summary>SMTP đã được cấu hình chưa (Smtp__User + Smtp__Pass).</summary>
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

// Gửi email qua SMTP bằng MailKit (bền hơn System.Net.Mail).
// Mặc định Gmail: thử cổng 465 (SSL) trước, nếu treo/lỗi thì thử 587 (STARTTLS) — cổng nào thông thì gửi được.
// Cấu hình bằng env trên Render: Smtp__User, Smtp__Pass (bắt buộc); Smtp__Host, Smtp__Port, Smtp__From (tùy chọn).
public class SmtpEmailSender : IAppEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    private string? User => _config["Smtp:User"];
    private string? Pass => _config["Smtp:Pass"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Pass);

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP chưa được cấu hình (thiếu Smtp__User / Smtp__Pass).");

        var host = _config["Smtp:Host"] ?? "smtp.gmail.com";
        var from = _config["Smtp:From"] ?? User!;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("CityScout", from));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // Nếu người dùng chỉ định cổng cụ thể thì dùng đúng cổng đó; không thì thử 465 rồi 587.
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
                await client.AuthenticateAsync(User, Pass);
                await client.SendAsync(msg);
                await client.DisconnectAsync(true);
                _logger.LogInformation("Đã gửi email tới {To} qua {Host}:{Port}", to, host, port);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning("Gửi mail qua {Host}:{Port} thất bại: {Msg}", host, port, ex.Message);
            }
        }
        throw last ?? new Exception("Không gửi được email qua SMTP.");
    }
}
