using MapHub.Models;

namespace MapHub.Services;

public interface IAiSuggestionService
{
    Task<AiPointSuggestion> SuggestAsync(string name, string? category, string? description, CancellationToken cancellationToken = default);
}
