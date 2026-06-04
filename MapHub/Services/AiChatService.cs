using System.Text;
using System.Text.Json;
using MapHub.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MapHub.Services;

public class AiChatService : IAiChatService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AiAssistantOptions> _options;
    private readonly ILogger<AiChatService> _logger;

    private const string SystemPrompt =
        "Bạn là trợ lý AI của CityScout — ứng dụng khám phá địa điểm tại Hà Nội.\n" +
        "Nhiệm vụ: Gợi ý địa điểm phù hợp và lập kế hoạch chuyến đi dựa trên dữ liệu thực tế được cung cấp.\n" +
        "Luôn trả lời bằng tiếng Việt. Thân thiện, ngắn gọn, thực tế.\n\n" +
        "QUY TẮC QUAN TRỌNG khi gợi ý địa điểm:\n" +
        "- Chỉ dùng các địa điểm trong danh sách [DỮ LIỆU ĐỊA ĐIỂM THỰC TẾ TRONG APP] được cung cấp\n" +
        "- Với mỗi địa điểm, PHẢI viết theo đúng định dạng: [Tên địa điểm](/Map/Details/ID)\n" +
        "  Ví dụ: [Hồ Hoàn Kiếm](/Map/Details/1), [Văn Miếu](/Map/Details/2)\n" +
        "- Nếu lập kế hoạch đi chơi, liệt kê theo buổi: Sáng / Trưa / Chiều / Tối\n" +
        "- Tối đa 200 từ. Không bịa đặt địa điểm ngoài danh sách.";

    public AiChatService(HttpClient httpClient, IOptionsMonitor<AiAssistantOptions> options, ILogger<AiChatService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return "AI chưa được cấu hình. Vui lòng liên hệ quản trị viên.";

        // Model ưu tiên: 2.5-flash (hoạt động với free tier mới)
        var modelsToTry = new[] { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.0-flash-lite" };
        if (!string.IsNullOrWhiteSpace(options.Model))
            modelsToTry = new[] { options.Model.Trim(), "gemini-2.5-flash", "gemini-2.0-flash" };

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = $"{SystemPrompt}\n\nNgười dùng hỏi: {prompt}" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.4,
                maxOutputTokens = 320
            }
        };
        var json = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        string? lastError = null;

        foreach (var model in modelsToTry)
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";

            try
            {
                using var response = await _httpClient.PostAsync(endpoint, json, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Gemini [{Model}] HTTP {Status}: {Error}", model, (int)response.StatusCode, err);

                    // Trích message từ JSON lỗi Gemini
                    try
                    {
                        using var errDoc = JsonDocument.Parse(err);
                        lastError = errDoc.RootElement
                            .GetProperty("error").GetProperty("message").GetString();
                    }
                    catch { lastError = $"HTTP {(int)response.StatusCode}"; }

                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(payload);

                var root = document.RootElement;
                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Gemini [{Model}] trả về không có candidates", model);
                    continue;
                }

                var text = candidates[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return string.IsNullOrWhiteSpace(text)
                    ? "AI không trả lời được. Vui lòng thử câu hỏi khác."
                    : text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini [{Model}] exception", model);
                lastError = ex.Message;
            }
        }

        return string.IsNullOrEmpty(lastError)
            ? "AI tạm thời không khả dụng. Vui lòng thử lại sau."
            : $"AI lỗi: {lastError}";
    }
}
