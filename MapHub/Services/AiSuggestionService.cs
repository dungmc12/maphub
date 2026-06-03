using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MapHub.Models;
using Microsoft.Extensions.Options;

namespace MapHub.Services;

public class AiSuggestionService : IAiSuggestionService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AiAssistantOptions> _options;
    private readonly ILogger<AiSuggestionService> _logger;

    public AiSuggestionService(
        HttpClient httpClient,
        IOptionsMonitor<AiAssistantOptions> options,
        ILogger<AiSuggestionService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<AiPointSuggestion> SuggestAsync(string name, string? category, string? description, CancellationToken cancellationToken = default)
    {
        var cleanName = (name ?? string.Empty).Trim();
        var cleanCategory = (category ?? string.Empty).Trim();
        var cleanDescription = (description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return new AiPointSuggestion
            {
                Category = string.IsNullOrWhiteSpace(cleanCategory) ? "Địa điểm" : cleanCategory,
                Description = string.IsNullOrWhiteSpace(cleanDescription) ? "Vui lòng nhập tên địa điểm để nhận gợi ý AI." : cleanDescription,
                Source = "heuristic"
            };
        }

        var options = _options.CurrentValue;
        var useExternalProvider = options.EnableExternalProvider &&
            !string.IsNullOrWhiteSpace(options.ApiKey) &&
            (!string.IsNullOrWhiteSpace(options.Endpoint) || IsGeminiModel(options.Model));

        if (useExternalProvider)
        {
            try
            {
                AiPointSuggestion external;
                if (IsGeminiModel(options.Model))
                {
                    external = await SuggestByGeminiAsync(cleanName, cleanCategory, cleanDescription, options, cancellationToken);
                }
                else
                {
                    external = await SuggestByExternalProviderAsync(cleanName, cleanCategory, cleanDescription, options, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(external.Category) && !string.IsNullOrWhiteSpace(external.Description))
                {
                    return external;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không lấy được gợi ý từ external AI provider. Chuyển sang heuristic fallback.");
            }
        }

        return SuggestByHeuristic(cleanName, cleanCategory, cleanDescription);
    }

    private async Task<AiPointSuggestion> SuggestByExternalProviderAsync(
        string name,
        string category,
        string description,
        AiAssistantOptions options,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(options.Model) ? "gpt-4o-mini" : options.Model,
            temperature = 0.25,
            max_tokens = 180,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Bạn là trợ lý AI cho app bản đồ. Trả về đúng JSON object với 2 field: category, description. category <= 40 ký tự, description <= 140 ký tự, tiếng Việt, súc tích."
                },
                new
                {
                    role = "user",
                    content = $"Tên địa điểm: {name}\nDanh mục hiện tại: {category}\nMô tả hiện tại: {description}\nHãy gợi ý category và description tốt hơn cho app bản đồ."
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return SuggestByHeuristic(name, category, description);
        }

        var jsonSnippet = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(jsonSnippet))
        {
            return SuggestByHeuristic(name, category, description);
        }

        using var suggestionDoc = JsonDocument.Parse(jsonSnippet);
        var root = suggestionDoc.RootElement;
        var suggestedCategory = root.TryGetProperty("category", out var c) ? c.GetString() : null;
        var suggestedDescription = root.TryGetProperty("description", out var d) ? d.GetString() : null;

        return new AiPointSuggestion
        {
            Category = (suggestedCategory ?? string.Empty).Trim(),
            Description = (suggestedDescription ?? string.Empty).Trim(),
            Source = "external"
        };
    }

    private async Task<AiPointSuggestion> SuggestByGeminiAsync(
        string name,
        string category,
        string description,
        AiAssistantOptions options,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(options.Model) ? "gemini-2.0-flash" : options.Model.Trim();
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";

        var prompt = $"Bạn là trợ lý AI cho app bản đồ. Trả về đúng JSON object với 2 field: category, description. category <= 40 ký tự, description <= 140 ký tự, tiếng Việt, súc tích. Tên địa điểm: {name}. Danh mục hiện tại: {category}. Mô tả hiện tại: {description}.";

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.25,
                maxOutputTokens = 180
            }
        };

        using var response = await _httpClient.PostAsync(
            endpoint,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var content = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return SuggestByHeuristic(name, category, description);
        }

        var jsonSnippet = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(jsonSnippet))
        {
            return SuggestByHeuristic(name, category, description);
        }

        using var suggestionDoc = JsonDocument.Parse(jsonSnippet);
        var root = suggestionDoc.RootElement;
        var suggestedCategory = root.TryGetProperty("category", out var c) ? c.GetString() : null;
        var suggestedDescription = root.TryGetProperty("description", out var d) ? d.GetString() : null;

        return new AiPointSuggestion
        {
            Category = (suggestedCategory ?? string.Empty).Trim(),
            Description = (suggestedDescription ?? string.Empty).Trim(),
            Source = "gemini"
        };
    }

    private static bool IsGeminiModel(string? model)
    {
        return !string.IsNullOrWhiteSpace(model)
            && model.Trim().StartsWith("gemini", StringComparison.OrdinalIgnoreCase);
    }

    private static AiPointSuggestion SuggestByHeuristic(string name, string category, string description)
    {
        var lower = name.ToLowerInvariant();

        var guessedCategory = category;
        if (string.IsNullOrWhiteSpace(guessedCategory))
        {
            guessedCategory = lower switch
            {
                _ when lower.Contains("cafe") || lower.Contains("coffee") => "Cà phê",
                _ when lower.Contains("museum") || lower.Contains("bao tang") || lower.Contains("bảo tàng") => "Văn hóa",
                _ when lower.Contains("park") || lower.Contains("công viên") => "Thiên nhiên",
                _ when lower.Contains("market") || lower.Contains("chợ") => "Mua sắm",
                _ when lower.Contains("restaurant") || lower.Contains("nhà hàng") || lower.Contains("quán") => "Ẩm thực",
                _ when lower.Contains("church") || lower.Contains("nhà thờ") || lower.Contains("chùa") => "Di sản",
                _ => "Địa điểm"
            };
        }

        var guessedDescription = description;
        if (string.IsNullOrWhiteSpace(guessedDescription))
        {
            guessedDescription = $"{name} là điểm {guessedCategory.ToLowerInvariant()} đáng tham khảo, phù hợp để thêm vào lịch trình khám phá trong CityScout.";
        }

        if (guessedDescription.Length > 140)
        {
            guessedDescription = guessedDescription[..140].TrimEnd() + "...";
        }

        return new AiPointSuggestion
        {
            Category = guessedCategory,
            Description = guessedDescription,
            Source = "heuristic"
        };
    }

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return content[start..(end + 1)];
    }
}
