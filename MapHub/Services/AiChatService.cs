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
        "Nhiệm vụ: Gợi ý địa điểm phù hợp và lập kế hoạch chuyến đi dựa trên dữ liệu thực tế.\n" +
        "Luôn trả lời bằng tiếng Việt. Thân thiện, thực tế, đầy đủ thông tin.\n\n" +
        "QUY TẮC bắt buộc:\n" +
        "- Ưu tiên dùng địa điểm trong [DỮ LIỆU ĐỊA ĐIỂM] được cung cấp\n" +
        "- Mỗi địa điểm PHẢI viết dạng link: [Tên địa điểm](/Map/Details/ID)\n" +
        "  Ví dụ: [Hồ Hoàn Kiếm](/Map/Details/1)\n" +
        "- Kế hoạch đi chơi: chia rõ **Sáng** / **Trưa** / **Chiều** / **Tối** với địa điểm cụ thể\n" +
        "- Trả lời đầy đủ, không bỏ dở giữa chừng. Tối đa 500 từ.";

    public AiChatService(HttpClient httpClient, IOptionsMonitor<AiAssistantOptions> options, ILogger<AiChatService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var apiKeys = options.AllKeys();
        if (apiKeys.Count == 0)
            return "AI chưa được cấu hình. Vui lòng liên hệ quản trị viên.";

        // Model ưu tiên: gemini-3.1-flash-lite (500 req/ngày free), fallback sang alias "-latest"
        var modelsToTry = new[] { "gemini-3.1-flash-lite", "gemini-flash-lite-latest", "gemini-flash-latest" };
        if (!string.IsNullOrWhiteSpace(options.Model))
            modelsToTry = new[] { options.Model.Trim(), "gemini-3.1-flash-lite", "gemini-flash-lite-latest", "gemini-flash-latest" }
                .Distinct().ToArray();

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
                temperature = 0.5,
                maxOutputTokens = 4096
            }
        };
        var json = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        string? lastError = null;

        // Thử lần lượt từng key × từng model. Key hết quota (429) → tự xoay sang key tiếp theo.
        for (int ki = 0; ki < apiKeys.Count; ki++)
        {
            var apiKey = apiKeys[ki];
            bool keyQuotaExhausted = false;

            foreach (var model in modelsToTry)
            {
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

                try
                {
                    using var response = await _httpClient.PostAsync(endpoint, json, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var err = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogWarning("Gemini key#{Key} [{Model}] HTTP {Status}: {Error}",
                            ki + 1, model, (int)response.StatusCode, err);

                        // Trích message từ JSON lỗi Gemini
                        try
                        {
                            using var errDoc = JsonDocument.Parse(err);
                            lastError = errDoc.RootElement
                                .GetProperty("error").GetProperty("message").GetString();
                        }
                        catch { lastError = $"HTTP {(int)response.StatusCode}"; }

                        // 429 = hết quota cho key này → bỏ các model còn lại, nhảy sang key sau
                        if ((int)response.StatusCode == 429)
                        {
                            keyQuotaExhausted = true;
                            break;
                        }
                        continue;
                    }

                    var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(payload);

                    var root = document.RootElement;
                    if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    {
                        _logger.LogWarning("Gemini key#{Key} [{Model}] trả về không có candidates", ki + 1, model);
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
                    _logger.LogError(ex, "Gemini key#{Key} [{Model}] exception", ki + 1, model);
                    lastError = ex.Message;
                }
            }

            if (keyQuotaExhausted && ki + 1 < apiKeys.Count)
                _logger.LogInformation("Key#{Key} hết quota, chuyển sang key#{Next}", ki + 1, ki + 2);
        }

        return string.IsNullOrEmpty(lastError)
            ? "AI tạm thời không khả dụng. Vui lòng thử lại sau."
            : $"AI lỗi: {lastError}";
    }
}
