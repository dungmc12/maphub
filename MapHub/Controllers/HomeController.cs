using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;

namespace MapHub.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;

        // Auto-expire events whose EndAt has passed
        var expiredEvents = await _context.Events
            .Where(e => e.EndAt.HasValue && e.EndAt < now && e.Status != "ended")
            .ToListAsync();
        foreach (var ev in expiredEvents)
            ev.Status = "ended";
        if (expiredEvents.Any())
            await _context.SaveChangesAsync();

        // Hero: only featured events that are not ended
        var events = await _context.Events
            .Where(e => e.IsFeatured && e.Status != "ended")
            .OrderBy(e => e.StartAt)
            .Take(6)
            .Include(e => e.Place)
            .ToListAsync();

        // Featured places (IsFeatured = true), fallback to recent if none
        var featuredPlaces = await _context.Places
            .Where(p => p.Visibility == "public" && p.IsApproved && p.IsFeatured)
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .Include(p => p.Reviews)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(8)
            .ToListAsync();

        if (!featuredPlaces.Any())
        {
            featuredPlaces = await _context.Places
                .Where(p => p.Visibility == "public" && p.IsApproved)
                .Include(p => p.Images.Where(i => i.IsPrimary))
                .Include(p => p.Reviews)
                .OrderByDescending(p => p.Id)
                .Take(8)
                .ToListAsync();
        }

        var feedPosts = await _context.FeedPosts
            .OrderByDescending(f => f.IsPinned)
            .ThenByDescending(f => f.PublishedAt)
            .Take(6)
            .ToListAsync();

        ViewBag.Events = events;
        ViewBag.FeedPosts = feedPosts;
        return View(featuredPlaces);
    }

    // Trang đọc bài viết kiểu Medium — public, theo slug (fallback id)
    [Route("bai-viet/{slug}")]
    public async Task<IActionResult> Article(string slug)
    {
        var post = await _context.FeedPosts.Include(f => f.Event)
            .FirstOrDefaultAsync(f => f.Slug == slug);
        if (post == null && int.TryParse(slug, out var pid))
            post = await _context.FeedPosts.Include(f => f.Event).FirstOrDefaultAsync(f => f.Id == pid);
        if (post == null) return NotFound();

        // Đếm lượt xem (không chặn render)
        post.ViewCount += 1;
        try { await _context.SaveChangesAsync(); } catch { }

        // Render Markdown → HTML
        var pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().UseSoftlineBreakAsHardlineBreak().Build();
        ViewBag.BodyHtml = string.IsNullOrWhiteSpace(post.Content)
            ? (string.IsNullOrWhiteSpace(post.Summary) ? "" : "<p>" + System.Net.WebUtility.HtmlEncode(post.Summary) + "</p>")
            : Markdig.Markdown.ToHtml(post.Content, pipeline);

        // Bài liên quan (cùng loại, mới nhất)
        ViewBag.Related = await _context.FeedPosts
            .Where(f => f.Id != post.Id && f.Type == post.Type)
            .OrderByDescending(f => f.PublishedAt)
            .Take(3).ToListAsync();

        ViewData["MetaDescription"] = post.SeoDescription ?? post.Summary;
        return View(post);
    }
}
