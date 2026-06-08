using MapHub.Data;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Controllers;

[AllowAnonymous]
[Route("Ai")]
public class AiController : Controller
{
    private readonly IAiSuggestionService _aiSuggestionService;
    private readonly ApplicationDbContext _db;

    public AiController(IAiSuggestionService aiSuggestionService, ApplicationDbContext db)
    {
        _aiSuggestionService = aiSuggestionService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> SuggestPoint(string name, string? category, string? description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Vui lòng nhập tên địa điểm trước khi dùng AI." });

        var suggestion = await _aiSuggestionService.SuggestAsync(name, category, description, cancellationToken);
        return Json(suggestion);
    }

    [HttpPost("Chat")]
    [AllowAnonymous]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, [FromServices] IAiChatService chatService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Vui lòng nhập câu hỏi." });

        // Lấy danh sách địa điểm thật từ DB để AI tham chiếu
        var places = await _db.Places
            .Where(p => p.IsApproved && p.Visibility == "public")
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.Id)   // địa điểm mới thêm/sửa luôn được đưa vào ngữ cảnh AI
            .Take(60)
            .Select(p => new { p.Id, p.Name, p.Category, p.Address })
            .ToListAsync(cancellationToken);

        var placesContext = string.Join("\n", places.Select(p =>
            $"- ID={p.Id} | {p.Name} | {p.Category} | {p.Address}"));

        var enrichedPrompt = $"[DỮ LIỆU ĐỊA ĐIỂM THỰC TẾ TRONG APP]\n{placesContext}\n\n[CÂU HỎI]\n{request.Prompt.Trim()}";

        var answer = await chatService.AskAsync(enrichedPrompt, cancellationToken);
        return Ok(new { answer });
    }

    public record ChatRequest(string Prompt);
}
