namespace MapHub.Models;

public class AiPointSuggestion
{
    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = "heuristic";
}
