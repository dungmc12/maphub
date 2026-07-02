using MapHub.Services;
using Microsoft.AspNetCore.Mvc;

namespace MapHub.Controllers;

[ApiController]
[Route("Health")]
public class HealthController : ControllerBase
{
    private readonly IAppEmailSender _emailSender;

    public HealthController(IAppEmailSender emailSender) => _emailSender = emailSender;

    [HttpGet]
    public IActionResult Get()
    {
        // email: true = đã cấu hình gửi mail (Brevo API hoặc SMTP); không lộ giá trị bí mật
        return Ok(new { status = "ok", email = _emailSender.IsConfigured, at = DateTime.UtcNow });
    }
}
