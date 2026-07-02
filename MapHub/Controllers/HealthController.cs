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
        // smtp: true = đã đặt env Smtp__User/Smtp__Pass (email xác thực hoạt động); không lộ giá trị bí mật
        return Ok(new { status = "ok", smtp = _emailSender.IsConfigured, at = DateTime.UtcNow });
    }
}
