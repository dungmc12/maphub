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

    // System prompt: trợ lý du lịch CityScout chuyên Hà Nội - Láng - Hòa Lạc
    private const string SystemPrompt =
        "Bạn là trợ lý du lịch AI của CityScout - ứng dụng khám phá địa điểm tại Hà Nội.\n" +
        "Nhiệm vụ: Gợi ý địa điểm ăn uống, vui chơi, tham quan và lập kế hoạch chuyến đi tại Hà Nội, " +
        "đặc biệt khu vực từ trung tâm Hà Nội đến Láng - Hòa Lạc (khu ĐH FPT).\n" +
        "Phong cách: Thân thiện, ngắn gọn, thực tế. Luôn trả lời bằng tiếng Việt.\n" +
        "Khi gợi ý địa điểm, hãy đề cập tên cụ thể, địa chỉ/khu vực và lý do phù hợp.\n" +
        "Một số địa điểm nổi bật trên CityScout:\n" +
        "- Hồ Hoàn Kiếm (Hoàn Kiếm): đi bộ, chụp ảnh\n" +
        "- Văn Miếu - Quốc Tử Giám (Đống Đa): tham quan văn hóa, vé 30k-70k\n" +
        "- Chùa Láng (Đống Đa): tâm linh, miễn phí\n" +
        "- Hồ Tây (Tây Hồ): cafe view hồ, đạp xe ven hồ\n" +
        "- Bún Chả Hương Liên (Hai Bà Trưng): đặc sản Hà Nội nổi tiếng thế giới\n" +
        "- Cafe Giảng - Egg Coffee (Hoàn Kiếm): cà phê trứng đặc sản\n" +
        "- Làng Văn hóa Du lịch các Dân tộc (Đồng Mô, Sơn Tây): dã ngoại, BBQ, kayak\n" +
        "- ĐH FPT Hà Nội (Hòa Lạc): kiến trúc đẹp, sinh viên tham quan\n" +
        "- Khu CNC Hòa Lạc: công nghệ\n" +
        "- Nhà hàng Gà Đồi Hòa Lạc: đặc sản vùng núi, nhóm đông\n" +
        "Giới hạn câu trả lời: Tối đa 180 từ. Không bịa đặt thông tin không chắc chắn.";

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

        // Thử các model theo thứ tự ưu tiên
        var modelsToTry = new[] { "gemini-2.0-flash", "gemini-1.5-flash", "gemini-1.5-flash-latest" };
        if (!string.IsNullOrWhiteSpace(options.Model))
            modelsToTry = new[] { options.Model.Trim() }.Concat(modelsToTry).Distinct().ToArray();

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
