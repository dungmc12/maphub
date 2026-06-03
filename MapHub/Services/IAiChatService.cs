namespace MapHub.Services;

public interface IAiChatService
{
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);
}
