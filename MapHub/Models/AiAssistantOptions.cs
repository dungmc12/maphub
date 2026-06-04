namespace MapHub.Models;

public class AiAssistantOptions
{
    public string ApiKey { get; set; } = string.Empty;

    // Key dự phòng — khi key chính hết quota (HTTP 429) sẽ tự xoay sang key tiếp theo
    public List<string> ApiKeys { get; set; } = new();

    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    public string Model { get; set; } = "gpt-4o-mini";

    public bool EnableExternalProvider { get; set; } = true;

    // Gộp ApiKey + ApiKeys, bỏ trống & trùng, giữ thứ tự ưu tiên
    public IReadOnlyList<string> AllKeys()
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApiKey)) keys.Add(ApiKey.Trim());
        foreach (var k in ApiKeys)
            if (!string.IsNullOrWhiteSpace(k) && !keys.Contains(k.Trim()))
                keys.Add(k.Trim());
        return keys;
    }
}
