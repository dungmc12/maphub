using MapHub.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Controllers;

// Blog công khai (SEO) — hiển thị các bài viết từ hệ thống Tin tức (FeedPost). Không đổi DB.
[AllowAnonymous]
public class BlogController : Controller
{
    private readonly ApplicationDbContext _context;
    public BlogController(ApplicationDbContext context) => _context = context;

    [HttpGet("/Blog")]
    public async Task<IActionResult> Index(string? type = null)
    {
        var query = _context.FeedPosts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.Type == type);

        var posts = await query
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.PublishedAt)
            .ToListAsync();

        ViewData["Title"] = "Blog — Cẩm nang khám phá Hà Nội";
        ViewData["MetaDescription"] = "Blog CityScout: cẩm nang, mẹo hay và tin tức khám phá địa điểm ăn uống, cà phê, vui chơi, văn hóa tại Hà Nội.";
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/Blog";
        ViewBag.Type = type;
        return View(posts);
    }

    [HttpGet("/Blog/{id:int}")]
    public async Task<IActionResult> Post(int id)
    {
        var post = await _context.FeedPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound();

        ViewData["Title"] = post.Title;
        var summary = post.Summary ?? post.Title;
        ViewData["MetaDescription"] = summary.Length > 160 ? summary.Substring(0, 157) + "…" : summary;
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/Blog/{post.Id}";
        return View(post);
    }
}
