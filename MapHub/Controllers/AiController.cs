using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MapHub.Controllers;

[AllowAnonymous]
[Route("Ai")]
public class AiController : Controller
{
    private readonly IAiSuggestionService _aiSuggestionService;

    public AiController(IAiSuggestionService aiSuggestionService)
    {
        _aiSuggestionService = aiSuggestionService;
    }

    [HttpGet]
    public async Task<IActionResult> SuggestPoint(string name, string? category, string? description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Vui lòng nhập tên địa điểm trước khi dùng AI." });
        }

        var suggestion = await _aiSuggestionService.SuggestAsync(name, category, description, cancellationToken);
        return Json(suggestion);
    }

    [HttpPost("Chat")]
    [AllowAnonymous]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, [FromServices] IAiChatService chatService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { message = "Vui long nhap cau hoi." });
        }

        var answer = await chatService.AskAsync(request.Prompt.Trim(), cancellationToken);
        return Ok(new { answer });
    }

    public record ChatRequest(string Prompt);
}
