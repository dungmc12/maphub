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

        // Ứng viên: ưu tiên địa điểm MỚI nhất (realtime) + nổi bật, kèm Tags/giá/giới thiệu để AI match đúng nhu cầu.
        var candidates = await _db.Places
            .Where(p => p.IsApproved && p.Visibility == "public")
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.Id)
            .Take(250)
            .Select(p => new {
                p.Id, p.Name, p.Category, p.Address, p.About, p.MinPrice, p.MaxPrice, p.IsFeatured,
                Tags = p.PlaceTags.Select(t => t.Tag!.Name).ToList()
            })
            .ToListAsync(cancellationToken);

        // Nhãn danh mục tiếng Việt để khớp từ khóa kiểu "cà phê", "nhà hàng"…
        var catVi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["restaurant"]="nhà hàng quán ăn", ["cafe"]="cà phê coffee", ["entertainment"]="vui chơi giải trí thể thao",
            ["hotel"]="khách sạn lưu trú resort nghỉ dưỡng", ["culture"]="văn hóa bảo tàng di tích",
            ["temple"]="tâm linh chùa đền", ["nature"]="thiên nhiên công viên hồ", ["education"]="giáo dục trường học"
        };

        // Tách từ khóa câu hỏi (bỏ dấu) để xếp hạng địa điểm liên quan nhất lên đầu.
        var keywords = Tokenize(request.Prompt);
        var ranked = candidates
            .Select(p => {
                var hay = Strip(string.Join(" ", new[] { p.Name, p.Category,
                    catVi.TryGetValue(p.Category ?? "", out var cv) ? cv : "",
                    p.Address, p.About, string.Join(" ", p.Tags) }));
                int score = keywords.Count(k => hay.Contains(k));
                return new { P = p, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.P.IsFeatured)
            .ThenByDescending(x => x.P.Id)
            .Take(60)
            .Select(x => x.P)
            .ToList();

        bool anyRelevant = keywords.Count > 0 && ranked.Take(8).Any(p => {
            var hay = Strip(string.Join(" ", new[] { p.Name, p.Category,
                catVi.TryGetValue(p.Category ?? "", out var cv) ? cv : "", p.Address, p.About, string.Join(" ", p.Tags) }));
            return keywords.Any(k => hay.Contains(k));
        });

        string Price(decimal? lo, decimal? hi) =>
            (lo == null && hi == null) ? "" : $" | giá {(lo.HasValue ? $"{lo:#,##0}" : "?")}–{(hi.HasValue ? $"{hi:#,##0}đ" : "?")}";

        var placesContext = string.Join("\n", ranked.Select(p =>
            $"- ID={p.Id} | {p.Name} | {p.Category} | {p.Address}" +
            (p.Tags.Any() ? $" | tags: {string.Join(", ", p.Tags)}" : "") + Price(p.MinPrice, p.MaxPrice)));

        var hint = anyRelevant
            ? "(Có địa điểm khớp nhu cầu trong danh sách — hãy ưu tiên dùng đúng loại đó.)"
            : "(Lưu ý: có thể KHÔNG có địa điểm đúng loại người dùng cần trong danh sách — nếu vậy hãy nói thẳng là dữ liệu app chưa có, ĐỪNG bịa địa điểm.)";

        var enrichedPrompt =
            $"[DỮ LIỆU ĐỊA ĐIỂM THỰC TẾ TRONG APP — chỉ được dùng các địa điểm này]\n{placesContext}\n\n" +
            $"{hint}\n\n[CÂU HỎI]\n{request.Prompt.Trim()}";

        var answer = await chatService.AskAsync(enrichedPrompt, cancellationToken);
        return Ok(new { answer });
    }

    public record ChatRequest(string Prompt);

    // Bỏ dấu tiếng Việt + lowercase để so khớp từ khóa không phụ thuộc dấu/hoa-thường.
    private static string Strip(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var norm = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (var c in norm)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant().Replace('đ', 'd');
    }

    // Bỏ stopword ngắn, lấy từ khóa có nghĩa từ câu hỏi (đã bỏ dấu).
    private static readonly HashSet<string> _stop = new(StringComparer.Ordinal) {
        "thi","sao","gi","la","co","khong","muon","di","den","cho","toi","minh","ban","cac","mot",
        "nao","o","dau","va","hay","cho","tai","khu","vuc","tim","kiem","cho","duoc","the","an"
    };
    private static List<string> Tokenize(string prompt)
        => Strip(prompt)
            .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2 && !_stop.Contains(w))
            .Distinct().ToList();
}
