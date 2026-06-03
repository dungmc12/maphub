using Microsoft.AspNetCore.Mvc;

namespace MapHub.Controllers;

[ApiController]
[Route("Health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", at = DateTime.UtcNow });
    }
}
