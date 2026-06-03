using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MapHub.Controllers;

[ApiController]
[Route("Weather")]
public class WeatherController : ControllerBase
{
    private readonly IWeatherService _weatherService;

    public WeatherController(IWeatherService weatherService)
    {
        _weatherService = weatherService;
    }

    [HttpGet("At")]
    [AllowAnonymous]
    public async Task<IActionResult> At(double lat, double lng, CancellationToken cancellationToken)
    {
        var snapshot = await _weatherService.GetSnapshotAsync(lat, lng, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(snapshot);
    }
}
