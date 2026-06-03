namespace MapHub.Models;

public class AiAssistantOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    public string Model { get; set; } = "gpt-4o-mini";

    public bool EnableExternalProvider { get; set; } = true;
}
