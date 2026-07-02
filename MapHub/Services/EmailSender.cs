using System.Net;
using System.Net.Mail;

namespace MapHub.Services;

public interface IAppEmailSender
{
    /// <summary>SMTP đã được cấu hình chưa (Smtp__User + Smtp__Pass).</summary>
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

// Gửi email qua SMTP (mặc định Gmail: smtp.gmail.com:587 + App Password).
// Cấu hình bằng env var trên Render: Smtp__User, Smtp__Pass (bắt buộc); Smtp__Host, Smtp__Port, Smtp__From (tùy chọn).
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
        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var from = _config["Smtp:From"] ?? User!;

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(User, Pass)
        };
        using var msg = new MailMessage
        {
            From = new MailAddress(from, "CityScout"),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        msg.To.Add(to);
        await client.SendMailAsync(msg);
        _logger.LogInformation("Đã gửi email tới {To}: {Subject}", to, subject);
    }
}
