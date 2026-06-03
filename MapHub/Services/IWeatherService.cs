namespace MapHub.Services;

public interface IWeatherService
{
    Task<WeatherSnapshot?> GetSnapshotAsync(double lat, double lng, CancellationToken cancellationToken = default);
}

public record WeatherSnapshot(double TemperatureC, double WindKph, string Summary, int WeatherCode);
