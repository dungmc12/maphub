using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MapHub.Services;

public class WeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;

    public WeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WeatherSnapshot?> GetSnapshotAsync(double lat, double lng, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lng}&current=temperature_2m,wind_speed_10m,weather_code";
        var response = await _httpClient.GetFromJsonAsync<OpenMeteoResponse>(url, cancellationToken);
        if (response?.Current is null)
        {
            return null;
        }

        var code = response.Current.WeatherCode;
        var summary = WeatherCodeToSummary(code);
        return new WeatherSnapshot(response.Current.Temperature, response.Current.WindSpeed, summary, code);
    }

    private static string WeatherCodeToSummary(int code)
    {
        return code switch
        {
            0 => "Trời quang",
            1 or 2 => "Ít mây",
            3 => "Nhiều mây",
            45 or 48 => "Sương mù",
            51 or 53 or 55 => "Mưa phùn",
            61 or 63 or 65 => "Mưa",
            71 or 73 or 75 => "Tuyết",
            80 or 81 or 82 => "Mưa rào",
            95 or 96 or 99 => "Giông sét",
            _ => "Thời tiết thay đổi"
        };
    }

    private sealed class OpenMeteoResponse
    {
        public CurrentWeather? Current { get; set; }
    }

    private sealed class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")]
        public double Temperature { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double WindSpeed { get; set; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; set; }
    }
}
