using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using ClosedXML.Excel;
using System.Globalization;

namespace MapHub.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(ApplicationDbContext context, IWebHostEnvironment env, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _env = env;
        _userManager = userManager;
    }

    // ──────────────── Nhập địa điểm hàng loạt bằng Excel ────────────────
    private static readonly string[] ImportHeaders = {
        "Tên (*)","Danh mục","Địa chỉ","Vĩ độ (*)","Kinh độ (*)","Điện thoại","Hotline",
        "Giờ mở","Giờ đóng","Giá từ","Giá đến","Website","Giới thiệu","Quyền xem","Ảnh (URL)"
    };

    [HttpGet]
    public IActionResult ImportPlaces() => View();

    [HttpGet]
    public IActionResult ImportPlacesTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DiaDiem");
        for (int i = 0; i < ImportHeaders.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = ImportHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891B2");
            c.Style.Font.FontColor = XLColor.White;
        }
        // Dòng ví dụ
        ws.Cell(2, 1).Value = "Quán Phở Bắc";
        ws.Cell(2, 2).Value = "restaurant";
        ws.Cell(2, 3).Value = "12 Phố Huế, Hai Bà Trưng, Hà Nội";
        ws.Cell(2, 4).Value = 21.012580;
        ws.Cell(2, 5).Value = 105.851380;
        ws.Cell(2, 6).Value = "0912345678";
        ws.Cell(2, 8).Value = "07:00";
        ws.Cell(2, 9).Value = "22:00";
        ws.Cell(2, 10).Value = 30000d;
        ws.Cell(2, 11).Value = 80000d;
        ws.Cell(2, 12).Value = "https://example.com";
        ws.Cell(2, 13).Value = "Phở bò gia truyền, không gian nhỏ ấm cúng.";
        ws.Cell(2, 14).Value = "public";
        ws.Cell(2, 15).Value = "https://images.unsplash.com/photo-xxxx";

        var help = wb.Worksheets.Add("HuongDan");
        help.Cell(1, 1).Value = "HƯỚNG DẪN NHẬP ĐỊA ĐIỂM";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(3, 1).Value = "• Cột có (*) là BẮT BUỘC: Tên, Vĩ độ, Kinh độ. Thiếu thì dòng đó bị bỏ qua.";
        help.Cell(4, 1).Value = "• Danh mục (mã hoặc tiếng Việt): restaurant=Nhà hàng, cafe=Cà phê, entertainment=Vui chơi, hotel=Lưu trú, culture=Văn hóa, temple=Tâm linh, nature=Thiên nhiên, education=Giáo dục, other=Khác.";
        help.Cell(5, 1).Value = "• Vĩ độ / Kinh độ: số thập phân, dùng dấu chấm (VD 21.0285). Lấy từ Google Maps.";
        help.Cell(6, 1).Value = "• Quyền xem: public (Công khai) hoặc private (Cá nhân). Bỏ trống = Công khai.";
        help.Cell(7, 1).Value = "• Giá từ / Giá đến: số nguyên (đồng), để trống nếu không có.";
        help.Cell(8, 1).Value = "• Ảnh (URL): dán đường dẫn ảnh (https://...jpg) làm ảnh đại diện; để trống nếu chưa có (bổ sung sau ở Sửa địa điểm).";
        help.Cell(9, 1).Value = "• XÓA dòng ví dụ (dòng 2) trước khi nhập dữ liệu thật.";
        help.Column(1).Width = 110;

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "mau-them-dia-diem.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ImportPlaces(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        { TempData["ImportErr"] = "Vui lòng chọn file Excel (.xlsx)."; return RedirectToAction(nameof(ImportPlaces)); }

        var uid = _userManager.GetUserId(User);
        var catMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["nhà hàng"]="restaurant", ["cà phê"]="cafe", ["ca phe"]="cafe", ["vui chơi"]="entertainment",
            ["lưu trú"]="hotel", ["văn hóa"]="culture", ["tâm linh"]="temple", ["thiên nhiên"]="nature",
            ["giáo dục"]="education", ["nhà riêng"]="home", ["công ty"]="work", ["trường học"]="school",
            ["yêu thích"]="favorite", ["khác"]="other"
        };
        var validCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase){
            "restaurant","cafe","entertainment","hotel","culture","temple","nature","education","home","work","school","favorite","other"
        };

        int added = 0, dup = 0; var errors = new List<string>();

        // Khóa chống trùng: tên (bỏ dấu) + toạ độ làm tròn ~11m. Nạp sẵn địa điểm đang có
        // để re-import cùng file KHÔNG tạo bản sao, và báo rõ số dòng bị bỏ qua.
        static string DupKey(string name, double lat, double lng)
            => $"{StripVn(name)}|{Math.Round(lat, 4)}|{Math.Round(lng, 4)}";
        var existingKeys = (await _context.Places
                .Select(p => new { p.Name, p.Latitude, p.Longitude }).ToListAsync())
            .Select(p => DupKey(p.Name, (double)p.Latitude, (double)p.Longitude))
            .ToHashSet();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            var used = ws.RangeUsed();
            if (used == null) { TempData["ImportErr"] = "File trống."; return RedirectToAction(nameof(ImportPlaces)); }

            foreach (var row in used.RowsUsed().Skip(1))   // bỏ dòng tiêu đề
            {
                string Str(int c) => row.Cell(c).GetString().Trim();
                double Num(int c)
                {
                    var cell = row.Cell(c);
                    if (cell.TryGetValue<double>(out var d)) return d;
                    var s = cell.GetString().Trim().Replace(",", ".");
                    return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : double.NaN;
                }

                var name = Str(1);
                if (string.IsNullOrWhiteSpace(name)) continue;
                int rn = row.RowNumber();
                double lat = Num(4), lng = Num(5);
                if (double.IsNaN(lat) || double.IsNaN(lng))
                { errors.Add($"Dòng {rn}: thiếu/sai toạ độ — bỏ qua."); continue; }

                // Trùng với địa điểm đã có (hoặc trùng trong chính file) → bỏ qua, không tạo bản sao
                var key = DupKey(name, lat, lng);
                if (!existingKeys.Add(key)) { dup++; continue; }

                var catRaw = Str(2);
                string cat = string.IsNullOrWhiteSpace(catRaw) ? "other"
                    : (catMap.TryGetValue(catRaw, out var cc) ? cc : (validCats.Contains(catRaw) ? catRaw.ToLower() : "other"));
                var visRaw = Str(14).ToLower();
                var vis = (visRaw.Contains("priv") || visRaw.Contains("cá nhân") || visRaw.Contains("ca nhan")) ? "private" : "public";
                double minN = Num(10), maxN = Num(11);

                var place = new Place {
                    Name = name, Category = cat, Address = NullIf(Str(3)),
                    Latitude = (decimal)lat, Longitude = (decimal)lng,
                    Phone = NullIf(Str(6)), Phone2 = NullIf(Str(7)),
                    OpenTime = NullIf(Str(8)), CloseTime = NullIf(Str(9)),
                    MinPrice = double.IsNaN(minN) ? null : (decimal?)minN,
                    MaxPrice = double.IsNaN(maxN) ? null : (decimal?)maxN,
                    WebsiteUrl = NullIf(Str(12)), About = NullIf(Str(13)),
                    Visibility = vis, IsApproved = true, CreatedByUserId = uid,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                // Ảnh đại diện theo URL (cột 15) — EF tự gắn FK khi lưu
                var imgUrl = Str(15);
                if (!string.IsNullOrWhiteSpace(imgUrl) && (imgUrl.StartsWith("http://") || imgUrl.StartsWith("https://")))
                    place.Images.Add(new PlaceImage { Url = imgUrl, IsPrimary = true, UploadedByUserId = uid });

                _context.Places.Add(place);
                added++;
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            TempData["ImportErr"] = "Lỗi đọc file: " + ex.Message;
            return RedirectToAction(nameof(ImportPlaces));
        }

        if (added == 0 && dup > 0)
            TempData["ImportErr"] = $"Không có địa điểm mới — {dup} dòng đã tồn tại (trùng tên + vị trí). File này có thể đã được nhập trước đó.";
        else
            TempData["ImportMsg"] = $"Đã nhập {added} địa điểm."
                + (dup > 0 ? $" Bỏ qua {dup} dòng trùng (đã có sẵn)." : "")
                + (errors.Any() ? " ⚠ " + string.Join(" ", errors.Take(8)) : "");
        return RedirectToAction(nameof(ImportPlaces));
    }

    private static string? NullIf(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // Bỏ dấu tiếng Việt + lowercase để so khớp tên không phụ thuộc dấu/hoa-thường
    private static string StripVn(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var norm = s.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (var c in norm)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant().Replace('đ', 'd');
    }

    public async Task<IActionResult> Index()
    {
        var pendingCount    = await _context.Places.CountAsync(p => p.Visibility == "public" && !p.IsApproved);
        var totalPlaces     = await _context.Places.CountAsync();
        var totalEvents     = await _context.Events.CountAsync();
        var totalUsers      = await _context.Users.CountAsync();
        var pendingPayments = await _context.Payments
            .Where(p => p.Status == "confirming")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.UserId, p.Amount, p.PlanType, p.Code, p.TransactionId, p.CreatedAt,
                               Email = _context.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault() })
            .ToListAsync();

        ViewBag.PendingCount    = pendingCount;
        ViewBag.TotalPlaces     = totalPlaces;
        ViewBag.TotalEvents     = totalEvents;
        ViewBag.TotalUsers      = totalUsers;
        // Dùng value tuple (kiểu public) thay anonymous → Razor view truy cập được, không 500
        ViewBag.PendingPayments = pendingPayments
            .Select(p => (p.Id, p.Email, p.UserId, p.Amount, p.PlanType, p.Code, p.CreatedAt)).ToList();
        ViewBag.Revenue = await _context.Payments.Where(p => p.Status == "paid").SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var now = DateTime.UtcNow;

        // Mặc định an toàn — nếu 1 truy vấn phân tích lỗi, dashboard vẫn render KPI (không 500)
        ViewBag.GrowthLabels = "[]"; ViewBag.GrowthData = "[]";
        ViewBag.CatLabels = "[]";    ViewBag.CatData = "[]";
        ViewBag.TopPlaces     = new List<(int Id, string Name, int Reviews, double Rating)>();
        ViewBag.PendingPlaces = new List<(int Id, string Name, string Category, DateTime CreatedAt)>();
        ViewBag.Activity      = new List<(string Kind, string Text, DateTime At)>();

        try
        {
            // Tăng trưởng: địa điểm tạo mỗi TUẦN (12 tuần gần nhất, tuần bắt đầu thứ 2, giờ VN +7)
            const int VN = 7;
            var vnToday = now.AddHours(VN).Date;
            var weekStart = vnToday.AddDays(-(((int)vnToday.DayOfWeek + 6) % 7));   // về thứ 2 đầu tuần
            var weeks = Enumerable.Range(0, 12).Select(i => weekStart.AddDays(-7 * (11 - i))).ToList();
            var placeDates = await _context.Places.Select(p => p.CreatedAt).ToListAsync();
            ViewBag.GrowthLabels = System.Text.Json.JsonSerializer.Serialize(weeks.Select(w => w.ToString("dd/MM")).ToList());
            ViewBag.GrowthData   = System.Text.Json.JsonSerializer.Serialize(weeks.Select(w => placeDates.Count(t => t.AddHours(VN).Date >= w && t.AddHours(VN).Date < w.AddDays(7))).ToList());

            // Phân bố theo danh mục
            var catVi = new Dictionary<string, string> {
                ["restaurant"]="Nhà hàng", ["cafe"]="Cà phê", ["entertainment"]="Vui chơi", ["hotel"]="Lưu trú",
                ["culture"]="Văn hóa", ["temple"]="Tâm linh", ["nature"]="Thiên nhiên", ["education"]="Giáo dục",
                ["home"]="Nhà riêng", ["work"]="Công ty", ["school"]="Trường học", ["favorite"]="Yêu thích"
            };
            var catGroups = await _context.Places.Where(p => p.Category != null)
                .GroupBy(p => p.Category!).Select(g => new { Cat = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(8).ToListAsync();
            ViewBag.CatLabels = System.Text.Json.JsonSerializer.Serialize(catGroups.Select(x => catVi.TryGetValue(x.Cat.ToLower(), out var l) ? l : x.Cat).ToList());
            ViewBag.CatData   = System.Text.Json.JsonSerializer.Serialize(catGroups.Select(x => x.Count).ToList());

            // Top địa điểm — chỉ ORDER BY Count trong SQL, tính rating trong bộ nhớ (tránh lỗi dịch SQL)
            var topQ = await _context.Places
                .Select(p => new { p.Id, p.Name, Reviews = p.Reviews.Count, RatingSum = p.Reviews.Sum(r => (int)r.QualityRating) })
                .OrderByDescending(x => x.Reviews).Take(5).ToListAsync();
            ViewBag.TopPlaces = topQ.Select(x => (x.Id, x.Name, x.Reviews,
                x.Reviews > 0 ? Math.Round((double)x.RatingSum / x.Reviews, 1) : 0.0)).ToList();

            // Chờ duyệt (địa điểm công khai)
            var pendQ = await _context.Places.Where(p => p.Visibility == "public" && !p.IsApproved)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new { p.Id, p.Name, p.Category, p.CreatedAt }).Take(6).ToListAsync();
            ViewBag.PendingPlaces = pendQ.Select(x => (x.Id, x.Name, x.Category ?? "", x.CreatedAt)).ToList();

            // Hoạt động gần đây (địa điểm mới + đánh giá mới + thanh toán)
            var acts = new List<(string Kind, string Text, DateTime At)>();
            foreach (var p in await _context.Places.OrderByDescending(x => x.CreatedAt).Take(6).Select(x => new { x.Name, x.CreatedAt }).ToListAsync())
                acts.Add(("place", $"Địa điểm mới: {p.Name}", p.CreatedAt));
            foreach (var r in await _context.PlaceReviews.OrderByDescending(x => x.CreatedAt).Take(6).Select(x => new { Name = x.Place!.Name, x.CreatedAt }).ToListAsync())
                acts.Add(("review", $"Đánh giá mới cho {r.Name}", r.CreatedAt));
            foreach (var pay in await _context.Payments.Where(x => x.Status == "paid").OrderByDescending(x => x.PaidAt).Take(4).Select(x => new { x.Amount, x.PaidAt }).ToListAsync())
                acts.Add(("pay", $"Thanh toán Pro {pay.Amount:#,##0}đ", pay.PaidAt ?? now));
            ViewBag.Activity = acts.OrderByDescending(a => a.At).Take(8).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Dashboard] analytics error: " + ex.Message);
        }

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Places(string? filter = null)
    {
        var query = _context.Places
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .AsQueryable();

        query = filter switch
        {
            "pending" => query.Where(p => p.Visibility == "public" && !p.IsApproved),
            "public"  => query.Where(p => p.Visibility == "public"),
            "private" => query.Where(p => p.Visibility == "private"),
            _         => query
        };

        var places = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        ViewBag.Filter = filter;
        // Đếm theo TOÀN BỘ bảng (không theo filter) để các tab luôn hiện đúng tổng
        ViewBag.CountAll     = await _context.Places.CountAsync();
        ViewBag.CountPending = await _context.Places.CountAsync(p => p.Visibility == "public" && !p.IsApproved);
        ViewBag.CountPublic  = await _context.Places.CountAsync(p => p.Visibility == "public");
        ViewBag.CountPrivate = await _context.Places.CountAsync(p => p.Visibility == "private");
        return View(places);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        place.IsApproved = true;
        place.UpdatedAt  = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã duyệt địa điểm: {place.Name}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        _context.Places.Remove(place);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã từ chối và xóa địa điểm.";
        return RedirectToAction(nameof(Index));
    }

    // ──────────────── Sinh đánh giá mẫu (demo) cho địa điểm ────────────────
    // KHÔNG copy Google. Tạo tài khoản người đánh giá mẫu + đánh giá tiếng Việt, RẢI ĐỀU mọi địa điểm
    // về cùng một mức, không trùng (user, địa điểm), điểm đa dạng thiên tích cực, ngày rải rác 6 tháng.
    // Bấm nhiều lần được: mỗi lần nâng "số đánh giá/địa điểm" là bù thêm cho đủ.
    private static readonly string[] ReviewerNames = {
        "Nguyễn Minh Anh","Trần Quốc Huy","Lê Thu Trang","Phạm Hoàng Nam","Hoàng Lan Phương",
        "Vũ Đức Anh","Đặng Bảo Ngọc","Bùi Việt Hùng","Đỗ Thùy Linh","Hồ Gia Bảo",
        "Ngô Khánh Vy","Dương Tuấn Kiệt","Lý Mai Chi","Phan Đình Phúc","Võ Ngọc Ánh",
        "Đinh Hải Đăng","Trịnh Thanh Hà","Mai Quang Dũng","Cao Thảo My","Đào Minh Quân",
        "Lâm Phương Thảo","Hà Tuấn Anh","Tô Ngọc Diệp","Chu Đăng Khoa","Nguyễn Hồng Nhung",
        "Trần Bá Lộc","Lê Khả Vy","Phạm Thùy Dung","Hoàng Anh Tú","Vũ Hải Yến",
        "Đặng Quốc Bảo","Bùi Thu Hiền","Đỗ Nhật Minh","Hồ Ngọc Mai","Ngô Thành Đạt",
        "Dương Kim Ngân","Lý Hoàng Long","Phan Thảo Vy","Võ Anh Khôi","Đinh Phương Anh",
        "Trịnh Gia Hân","Mai Đức Thịnh","Cao Thị Ngọc","Đào Văn Nam","Lâm Bảo Trâm",
        "Hà Minh Đức","Tô Thanh Tùng","Chu Diệu Linh","Nguyễn Tiến Dũng","Trần Yến Nhi",
    };

    private static readonly string[] ReviewGeneric = {
        "Không gian đẹp, sạch sẽ, nhân viên thân thiện. Sẽ quay lại.",
        "Vị trí dễ tìm, phục vụ nhanh. Mình khá hài lòng.",
        "Trải nghiệm ổn, giá hợp lý so với chất lượng.",
        "Chỗ này được, phù hợp đi cùng bạn bè và gia đình.",
        "Nhân viên nhiệt tình, hỏi gì cũng tư vấn kỹ.",
        "Khá đông vào cuối tuần nhưng phục vụ vẫn chu đáo.",
        "Lần đầu tới thấy ưng, không gian thoáng và yên tĩnh.",
        "Mọi thứ đều ok, chỉ hơi khó gửi xe một chút.",
        "Đáng đồng tiền, mình đánh giá cao thái độ phục vụ.",
        "Sạch sẽ, gọn gàng, view chụp ảnh đẹp.",
    };
    private static readonly string[] ReviewFood = {
        "Đồ ăn ngon, phần ăn đầy đặn, giá mềm.",
        "Món ăn đậm đà, hợp khẩu vị, đồ uống cũng ổn.",
        "Quán sạch, món ra nhanh, sẽ giới thiệu bạn bè.",
        "Hương vị tròn vị, nêm nếm vừa miệng.",
        "Menu đa dạng, có nhiều lựa chọn cho nhóm đông.",
        "Đồ uống pha ngon, bánh ngọt cũng đáng thử.",
        "Giá hợp lý, chất lượng món ổn định qua nhiều lần ghé.",
        "Phục vụ nhanh, món nóng sốt, không phải chờ lâu.",
    };
    private static readonly string[] ReviewNature = {
        "Khung cảnh thiên nhiên đẹp, không khí trong lành.",
        "Rất hợp để đi dạo, thư giãn cuối tuần.",
        "Yên tĩnh, thoáng mát, chụp ảnh cực đẹp.",
        "Không gian xanh, thích hợp cả nhà đi chơi.",
        "Buổi chiều ra đây hóng gió rất dễ chịu.",
    };
    private static readonly string[] ReviewCulture = {
        "Địa điểm ý nghĩa, nhiều thông tin bổ ích.",
        "Kiến trúc đẹp, được bảo tồn tốt, đáng tham quan.",
        "Không gian trang nghiêm, sạch sẽ, đáng để ghé.",
        "Trải nghiệm văn hoá thú vị, học hỏi được nhiều.",
    };

    // Đảm bảo có sẵn "count" tài khoản người đánh giá mẫu (idempotent), trả về danh sách UserId theo thứ tự.
    private async Task<List<string>> EnsureReviewersAsync(int count)
    {
        count = Math.Clamp(count, 1, ReviewerNames.Length);
        var ids = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var email = $"reviewer{i + 1}@cityscout.local";
            var u = await _userManager.FindByEmailAsync(email);
            if (u == null)
            {
                u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                var res = await _userManager.CreateAsync(u, "Demo@12345");
                if (!res.Succeeded) continue;
            }
            if (await _context.UserProfiles.FindAsync(u.Id) == null)
            {
                _context.UserProfiles.Add(new UserProfile { UserId = u.Id, DisplayName = ReviewerNames[i], Tier = "free", MaxPlaces = 3, MaxPlans = 3 });
                await _context.SaveChangesAsync();
            }
            ids.Add(u.Id);
        }
        return ids;
    }

    // Nâng 1 tài khoản lên Pro (hồ sơ + role) — dùng cho seed người Pro/đơn hàng
    private async Task MakeProAsync(string userId, string displayName)
    {
        var p = await _context.UserProfiles.FindAsync(userId);
        if (p == null) { p = new UserProfile { UserId = userId, DisplayName = displayName }; _context.UserProfiles.Add(p); }
        p.Tier = "pro";
        p.ProExpiresAt = DateTime.UtcNow.AddYears(1);
        p.MaxPlaces = int.MaxValue; p.MaxPlans = int.MaxValue; p.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        var u = await _userManager.FindByIdAsync(userId);
        if (u != null && !await _userManager.IsInRoleAsync(u, "Pro"))
            await _userManager.AddToRoleAsync(u, "Pro");
    }

    // Sinh đánh giá mẫu — RẢI ĐỀU: đưa MỌI địa điểm lên cùng mức "perPlace". Bấm lại với số lớn hơn để tăng dần.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedReviews(int perPlace = 10)
    {
        perPlace = Math.Clamp(perPlace, 1, ReviewerNames.Length);

        // Cần ít nhất "perPlace" người đánh giá khác nhau cho mỗi địa điểm
        var reviewerIds = await EnsureReviewersAsync(Math.Min(ReviewerNames.Length, perPlace + 5));
        if (reviewerIds.Count == 0)
        { TempData["Error"] = "Không tạo được tài khoản đánh giá mẫu."; return RedirectToAction(nameof(Places)); }

        var rnd = new Random();
        int quality() { var r = rnd.NextDouble(); return r < 0.55 ? 5 : r < 0.85 ? 4 : r < 0.96 ? 3 : 2; }
        string[] pool(string? cat) => (cat ?? "").ToLower() switch {
            "restaurant" or "cafe" => ReviewFood,
            "nature" => ReviewNature,
            "culture" or "temple" => ReviewCulture,
            _ => ReviewGeneric
        };

        var places = await _context.Places.Select(p => new { p.Id, p.Category }).ToListAsync();
        int addedReviews = 0, toppedPlaces = 0;
        foreach (var p in places)
        {
            var existingUserIds = await _context.PlaceReviews.Where(r => r.PlaceId == p.Id)
                .Select(r => r.UserId).ToListAsync();
            int have = existingUserIds.Count;
            if (have >= perPlace) continue;   // đã đủ mức → giữ nguyên (đều)

            var avail = reviewerIds.Where(id => !existingUserIds.Contains(id)).OrderBy(_ => rnd.Next()).ToList();
            int need = Math.Min(perPlace - have, avail.Count);
            var texts = pool(p.Category);
            bool isFood = (p.Category ?? "").ToLower() is "restaurant" or "cafe";
            for (int i = 0; i < need; i++)
            {
                int q = quality();
                int s = Math.Max(2, Math.Min(5, q + rnd.Next(-1, 2)));
                _context.PlaceReviews.Add(new PlaceReview {
                    PlaceId = p.Id, UserId = avail[i],
                    QualityRating = (byte)q, ServiceRating = (byte)s,
                    FoodRating = isFood ? (byte?)Math.Max(2, Math.Min(5, q + rnd.Next(-1, 2))) : null,
                    Content = texts[rnd.Next(texts.Length)],
                    CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(1, 180)).AddHours(-rnd.Next(0, 24))
                });
                addedReviews++;
            }
            if (need > 0) toppedPlaces++;
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"Đã thêm {addedReviews} đánh giá cho {toppedPlaces} địa điểm — mọi địa điểm giờ ở mức {perPlace} đánh giá.";
        return RedirectToAction(nameof(Places));
    }

    // Bật/tắt "chế độ demo" — khi TẮT thì mọi nút tạo dữ liệu mẫu bị ẩn (chụp cho hội đồng không lộ).
    // Chỉ bật bằng cách truy cập đường link này (chỉ bạn biết): /Admin/ToggleDemoTools
    [HttpGet]
    public IActionResult ToggleDemoTools(string? returnUrl = null)
    {
        var on = Request.Cookies["demo_tools"] == "1";
        if (on) Response.Cookies.Delete("demo_tools");
        else Response.Cookies.Append("demo_tools", "1",
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(30), IsEssential = true });
        TempData["Success"] = on ? "Đã ẨN công cụ demo (an toàn để chụp/hội đồng xem)." : "Đã HIỆN công cụ demo — chỉ mình bạn thấy trên máy này.";
        return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/Admin/Places" : returnUrl);
    }

    // Ngày bắt đầu rải đơn mẫu: 03/06 (khi dự án bắt đầu có dữ liệu). Đơn mẫu nhận diện qua TransactionId "SEED-".
    private static readonly DateTime SeedStartDate = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);

    // Các tài khoản THẬT đủ điều kiện gắn đơn Pro mẫu (loại tài khoản demo @cityscout.local và Admin).
    private async Task<List<(string Id, string Name)>> RealBuyerAccountsAsync(int take)
    {
        var adminIds = (await _context.UserProfiles.Where(p => p.Tier == "admin").Select(p => p.UserId).ToListAsync()).ToHashSet();
        var users = await _context.Users
            .Where(u => u.Email != null && !u.Email.EndsWith("@cityscout.local"))
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync();
        return users.Where(u => !adminIds.Contains(u.Id))
            .Take(take)
            .Select(u => (u.Id, (u.Email ?? "").Split('@')[0]))
            .ToList();
    }

    // Hạ về Free các user vừa xoá đơn mẫu — CHỈ khi họ không còn đơn thật nào (giữ nguyên khách Pro thật & Admin).
    private async Task DowngradeSeedBuyersAsync(List<string> userIds)
    {
        foreach (var id in userIds.Distinct())
        {
            bool hasReal = await _context.Payments.AnyAsync(p => p.UserId == id && p.Status == "paid"
                && (p.TransactionId == null || !p.TransactionId.StartsWith("SEED-")));
            if (hasReal) continue;
            var p = await _context.UserProfiles.FindAsync(id);
            if (p == null || p.Tier == "admin") continue;   // không đụng Admin
            p.Tier = "free"; p.ProExpiresAt = null; p.MaxPlaces = 3; p.MaxPlans = 3; p.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            var u = await _userManager.FindByIdAsync(id);
            if (u != null && await _userManager.IsInRoleAsync(u, "Pro"))
                await _userManager.RemoveFromRoleAsync(u, "Pro");
        }
    }

    // Sinh ĐƠN PRO mẫu gắn vào TÀI KHOẢN THẬT — rải đều doanh thu từ 03/06 tới nay (không ngày tương lai).
    // Đơn hiển thị như payos thật (email thật); bấm lại là xoá đơn mẫu cũ rồi tạo lại rải đều.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedPayments(int orders = 20)
    {
        orders = Math.Clamp(orders, 1, 100);
        const decimal amount = 59000m;   // giá gói tháng

        var buyers = await RealBuyerAccountsAsync(orders);
        if (buyers.Count == 0)
        {
            TempData["Error"] = "Chưa có tài khoản thật nào (ngoài Admin) để gắn đơn Pro. Hãy đăng ký vài tài khoản trước.";
            return RedirectToAction(nameof(Revenue));
        }

        // Ghi nhớ các buyer của đơn mẫu cũ → xoá đơn mẫu cũ → hạ họ về Free (nếu không còn đơn thật)
        var prevSeedUsers = await _context.Payments
            .Where(p => p.TransactionId != null && p.TransactionId.StartsWith("SEED-"))
            .Select(p => p.UserId).Distinct().ToListAsync();
        var oldSeed = await _context.Payments.Where(p => p.TransactionId != null && p.TransactionId.StartsWith("SEED-")).ToListAsync();
        _context.Payments.RemoveRange(oldSeed);
        await _context.SaveChangesAsync();
        await DowngradeSeedBuyersAsync(prevSeedUsers);

        // Rải đều PaidAt từ 03/06 → hôm nay (kẹp trong khoảng, không vượt hiện tại)
        var now = DateTime.UtcNow;
        var start = SeedStartDate < now ? SeedStartDate : now.AddDays(-42);
        double totalDays = (now - start).TotalDays;
        var rnd = new Random();
        int made = 0;
        for (int i = 0; i < buyers.Count; i++)
        {
            double frac = (i + 0.5) / buyers.Count;
            double jitter = (rnd.NextDouble() - 0.5) * (totalDays / buyers.Count) * 0.5;
            var paidAt = start.AddDays(frac * totalDays + jitter);
            if (paidAt > now) paidAt = now.AddHours(-rnd.Next(1, 12));
            if (paidAt < start) paidAt = start;
            _context.Payments.Add(new Payment {
                UserId = buyers[i].Id, Amount = amount, PlanType = "month",
                Provider = "payos", Status = "paid",
                Code = $"CSPRO{200 + i}", TransactionId = $"SEED-{Guid.NewGuid():N}"[..16],
                Description = "Nâng cấp Pro (gói tháng)",
                CreatedAt = paidAt, PaidAt = paidAt
            });
            await MakeProAsync(buyers[i].Id, buyers[i].Name);
            made++;
        }
        await _context.SaveChangesAsync();

        var note = made < orders ? $" (chỉ có {made} tài khoản thật khả dụng)" : "";
        TempData["Success"] = $"Đã tạo {made} đơn Pro cho tài khoản thật (mỗi đơn {amount:#,##0}đ) rải đều từ {start:dd/MM} đến nay{note}.";
        return RedirectToAction(nameof(Revenue));
    }

    // Xoá HẾT đơn Pro mẫu + hạ các tài khoản đó về Free (giữ nguyên khách Pro thật & Admin)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearSeedPayments()
    {
        var seedUsers = await _context.Payments
            .Where(p => p.TransactionId != null && p.TransactionId.StartsWith("SEED-"))
            .Select(p => p.UserId).Distinct().ToListAsync();
        var seed = await _context.Payments.Where(p => p.TransactionId != null && p.TransactionId.StartsWith("SEED-")).ToListAsync();
        _context.Payments.RemoveRange(seed);
        await _context.SaveChangesAsync();
        await DowngradeSeedBuyersAsync(seedUsers);
        TempData["Success"] = $"Đã xoá {seed.Count} đơn Pro mẫu và hạ các tài khoản đó về Free.";
        return RedirectToAction(nameof(Revenue));
    }

    // ── Events ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Events()
    {
        var events = await _context.Events
            .Include(e => e.Place)
            .OrderByDescending(e => e.StartAt)
            .ToListAsync();
        return View(events);
    }

    [HttpGet]
    public async Task<IActionResult> CreateEvent()
    {
        ViewBag.Places = await _context.Places
            .Where(p => p.Visibility == "public" && p.IsApproved)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude })
            .ToListAsync();
        return View(new Event { StartAt = DateTime.Now });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEvent(Event ev, IFormFile? BannerFile)
    {
        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        ev.CreatedByUserId = adminId;

        // Ép Kind=Utc cho cột timestamptz của Postgres (form trả Unspecified -> sẽ lỗi 500)
        ev.StartAt = DateTime.SpecifyKind(ev.StartAt, DateTimeKind.Utc);
        if (ev.EndAt.HasValue) ev.EndAt = DateTime.SpecifyKind(ev.EndAt.Value, DateTimeKind.Utc);

        // File upload takes priority over URL
        if (BannerFile != null && BannerFile.Length > 0)
            ev.BannerImageUrl = await SaveUploadAsync(BannerFile, "events");

        _context.Events.Add(ev);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã tạo sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpGet]
    public async Task<IActionResult> EditEvent(int id)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev == null) return NotFound();
        ViewBag.Places = await _context.Places
            .Where(p => p.Visibility == "public" && p.IsApproved)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude })
            .ToListAsync();
        return View(ev);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEvent(Event ev, IFormFile? BannerFile)
    {
        var existing = await _context.Events.FindAsync(ev.Id);
        if (existing == null) return NotFound();

        existing.Title       = ev.Title;
        existing.Description = ev.Description;
        existing.PlaceId     = ev.PlaceId;
        existing.Latitude    = ev.Latitude;
        existing.Longitude   = ev.Longitude;
        existing.StartAt     = DateTime.SpecifyKind(ev.StartAt, DateTimeKind.Utc);
        existing.EndAt       = ev.EndAt.HasValue ? DateTime.SpecifyKind(ev.EndAt.Value, DateTimeKind.Utc) : null;
        existing.Status      = ev.Status;
        existing.IsFeatured  = ev.IsFeatured;

        // File upload first, then URL if provided, else keep existing
        if (BannerFile != null && BannerFile.Length > 0)
            existing.BannerImageUrl = await SaveUploadAsync(BannerFile, "events");
        else if (!string.IsNullOrWhiteSpace(ev.BannerImageUrl))
            existing.BannerImageUrl = ev.BannerImageUrl;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev != null) { _context.Events.Remove(ev); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa sự kiện.";
        return RedirectToAction(nameof(Events));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFeaturedPlace(int id)
    {
        var place = await _context.Places.FindAsync(id);
        if (place == null) return NotFound();
        place.IsFeatured = !place.IsFeatured;
        place.UpdatedAt  = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Success"] = place.IsFeatured
            ? $"Đã đưa \"{place.Name}\" vào danh sách nổi bật."
            : $"Đã bỏ \"{place.Name}\" khỏi danh sách nổi bật.";
        return RedirectToAction(nameof(Index));
    }

    // ── Feed Posts ───────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> FeedPosts()
    {
        var posts = await _context.FeedPosts
            .OrderByDescending(f => f.IsPinned)
            .ThenByDescending(f => f.PublishedAt)
            .ToListAsync();
        return View(posts);
    }

    [HttpGet]
    public async Task<IActionResult> CreateFeedPost()
    {
        ViewBag.Events = await _context.Events.OrderByDescending(e => e.StartAt)
            .Select(e => new { e.Id, e.Title }).ToListAsync();
        return View(new FeedPost());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFeedPost(FeedPost post, IFormFile? imageFile)
    {
        post.PublishedAt = DateTime.UtcNow;
        post.Slug = MakeSlug(string.IsNullOrWhiteSpace(post.Slug) ? post.Title : post.Slug);
        // Ảnh bìa: ưu tiên file upload, sau đó URL
        if (imageFile != null && imageFile.Length > 0)
            post.CoverImageUrl = await SaveUploadAsync(imageFile, "feed");

        _context.FeedPosts.Add(post);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã tạo bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post != null) { _context.FeedPosts.Remove(post); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePinFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post == null) return NotFound();
        post.IsPinned = !post.IsPinned;
        await _context.SaveChangesAsync();
        TempData["Success"] = post.IsPinned ? $"Đã ghim \"{post.Title}\"." : $"Đã bỏ ghim \"{post.Title}\".";
        return RedirectToAction(nameof(FeedPosts));
    }

    [HttpGet]
    public async Task<IActionResult> EditFeedPost(int id)
    {
        var post = await _context.FeedPosts.FindAsync(id);
        if (post == null) return NotFound();
        ViewBag.Events = await _context.Events.OrderByDescending(e => e.StartAt)
            .Select(e => new { e.Id, e.Title }).ToListAsync();
        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFeedPost(FeedPost post, IFormFile? imageFile)
    {
        var existing = await _context.FeedPosts.FindAsync(post.Id);
        if (existing == null) return NotFound();
        existing.Title         = post.Title;
        existing.Slug          = MakeSlug(string.IsNullOrWhiteSpace(post.Slug) ? post.Title : post.Slug);
        existing.Summary       = post.Summary;
        existing.Content       = post.Content;
        existing.Type          = post.Type;
        existing.SeoTitle      = post.SeoTitle;
        existing.SeoDescription= post.SeoDescription;
        existing.IsPinned      = post.IsPinned;
        existing.EventId       = post.EventId;
        // Ảnh bìa: file mới ưu tiên, rồi URL, để trống = giữ nguyên
        if (imageFile != null && imageFile.Length > 0)
            existing.CoverImageUrl = await SaveUploadAsync(imageFile, "feed");
        else if (!string.IsNullOrWhiteSpace(post.CoverImageUrl))
            existing.CoverImageUrl = post.CoverImageUrl;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật bài viết.";
        return RedirectToAction(nameof(FeedPosts));
    }

    // ── Place management ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> AddPlace()
    {
        ViewBag.AllTags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPlace(Place place, string? imageUrl, IFormFile? imageFile, int[]? tagIds,
        IFormFile[]? moreImages = null, IFormFile[]? menuImages = null, IFormFile? videoFile = null)
    {
        place.IsApproved      = true;
        place.CreatedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        place.CreatedAt       = DateTime.UtcNow;
        place.UpdatedAt       = DateTime.UtcNow;
        _context.Places.Add(place);
        await _context.SaveChangesAsync();

        // Tags
        if (tagIds?.Any() == true)
        {
            var validTagIds = await _context.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            foreach (var tid in validTagIds)
                _context.PlaceTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tid });
        }

        // Ảnh: ưu tiên file upload, sau đó URL
        string? resolvedUrl = null;
        if (imageFile != null && imageFile.Length > 0)
            resolvedUrl = await SaveUploadAsync(imageFile, "places");
        else if (!string.IsNullOrWhiteSpace(imageUrl))
            resolvedUrl = imageUrl;
        if (resolvedUrl != null)
            _context.PlaceImages.Add(new PlaceImage { PlaceId = place.Id, Url = resolvedUrl, IsPrimary = true });
        await _context.SaveChangesAsync();

        // Nhiều ảnh khác + ảnh menu (không bắt buộc)
        await AddPlaceImagesAsync(place.Id, moreImages, menuImages, place.CreatedByUserId, videoFile);

        TempData["Success"] = $"Đã thêm địa điểm: {place.Name}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditPlace(int id)
    {
        var place = await _context.Places
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();
        ViewBag.AllTags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        ViewBag.SelectedTagIds = place.PlaceTags.Select(pt => pt.TagId).ToHashSet();
        return View(place);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlace(int id, string name, string? category, string? about,
        string? address, string? phone, string? websiteUrl, decimal? minPrice, decimal? maxPrice,
        string visibility, string? newImageUrl, IFormFile? imageFile, int[]? tagIds, bool isFeatured = false,
        IFormFile[]? moreImages = null, IFormFile[]? menuImages = null,
        string? phone2 = null, string? openTime = null, string? closeTime = null, IFormFile? videoFile = null)
    {
        var place = await _context.Places.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
        if (place == null) return NotFound();

        // Cập nhật tags (thêm cái mới tick, bỏ cái bỏ tick)
        var newTagIds = (tagIds ?? Array.Empty<int>()).ToHashSet();
        var currentTags = await _context.PlaceTags.Where(pt => pt.PlaceId == id).ToListAsync();
        _context.PlaceTags.RemoveRange(currentTags.Where(pt => !newTagIds.Contains(pt.TagId)));
        var existingTagIds = currentTags.Select(pt => pt.TagId).ToHashSet();
        foreach (var tid in newTagIds.Where(t => !existingTagIds.Contains(t)))
            _context.PlaceTags.Add(new PlaceTag { PlaceId = id, TagId = tid });

        place.Name       = name;
        place.Category   = category;
        place.About      = about;
        place.Address    = address;
        place.Phone      = phone;
        place.Phone2     = phone2;
        place.OpenTime   = openTime;
        place.CloseTime  = closeTime;
        place.WebsiteUrl = websiteUrl;
        place.MinPrice   = minPrice;
        place.MaxPrice   = maxPrice;
        place.Visibility = visibility;
        place.IsFeatured = isFeatured;
        place.UpdatedAt  = DateTime.UtcNow;

        // Determine new image URL
        string? resolvedUrl = null;
        if (imageFile != null && imageFile.Length > 0)
            resolvedUrl = await SaveUploadAsync(imageFile, "places");
        else if (!string.IsNullOrWhiteSpace(newImageUrl))
            resolvedUrl = newImageUrl;

        if (resolvedUrl != null)
        {
            var primary = place.Images.FirstOrDefault(i => i.IsPrimary);
            if (primary != null) primary.Url = resolvedUrl;
            else _context.PlaceImages.Add(new PlaceImage { PlaceId = place.Id, Url = resolvedUrl, IsPrimary = true });
        }
        await _context.SaveChangesAsync();

        // Bổ sung nhiều ảnh khác + ảnh menu (không bắt buộc)
        await AddPlaceImagesAsync(place.Id, moreImages, menuImages, place.CreatedByUserId, videoFile);

        TempData["Success"] = $"Đã cập nhật: {place.Name}";
        return RedirectToAction(nameof(Places));   // về trang Quản lý địa điểm, không đá ra dashboard
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlace(int id, string? filter = null)
    {
        var place = await _context.Places.FindAsync(id);
        if (place != null) { _context.Places.Remove(place); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa địa điểm.";
        return RedirectToAction(nameof(Places), new { filter });
    }

    // Xóa nhiều địa điểm cùng lúc (chọn checkbox)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlaces(int[]? ids, string? filter = null)
    {
        if (ids == null || ids.Length == 0)
        {
            TempData["Success"] = "Chưa chọn địa điểm nào để xóa.";
            return RedirectToAction(nameof(Places), new { filter });
        }
        var places = await _context.Places.Where(p => ids.Contains(p.Id)).ToListAsync();
        _context.Places.RemoveRange(places);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa {places.Count} địa điểm.";
        return RedirectToAction(nameof(Places), new { filter });
    }

    // ── Tag management ───────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Tags()
    {
        var tags = await _context.Tags
            .Include(t => t.PlaceTags)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return View(tags);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Tên tag không được để trống.";
            return RedirectToAction(nameof(Tags));
        }
        if (await _context.Tags.AnyAsync(t => t.Name == name.Trim()))
        {
            TempData["Error"] = "Tag này đã tồn tại.";
            return RedirectToAction(nameof(Tags));
        }
        _context.Tags.Add(new Tag { Name = name.Trim() });
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã tạo tag: {name.Trim()}";
        return RedirectToAction(nameof(Tags));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var tag = await _context.Tags.FindAsync(id);
        if (tag != null) { _context.Tags.Remove(tag); await _context.SaveChangesAsync(); }
        TempData["Success"] = "Đã xóa tag.";
        return RedirectToAction(nameof(Tags));
    }

    // ── User Management ──────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Users(string? q = null)
    {
        var query = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email!.Contains(q) || u.UserName!.Contains(q));

        var users = await query.OrderBy(u => u.Email).ToListAsync();
        var userRoles = new Dictionary<string, IList<string>>();
        foreach (var u in users)
            userRoles[u.Id] = await _userManager.GetRolesAsync(u);

        var placeCounts = await _context.Places
            .Where(p => p.CreatedByUserId != null)
            .GroupBy(p => p.CreatedByUserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        // Trạng thái Pro/Admin THẬT theo UserProfile (cái user thực sự đang hưởng) — để admin
        // thấy đúng kể cả khi role & profile lệch nhau, và bấm "Bỏ Pro" sửa được.
        var now = DateTime.UtcNow;
        var proUserIds = (await _context.UserProfiles
            .Where(p => p.Tier == "pro" && (p.ProExpiresAt == null || p.ProExpiresAt > now))
            .Select(p => p.UserId).ToListAsync()).ToHashSet();
        var adminUserIds = (await _context.UserProfiles
            .Where(p => p.Tier == "admin")
            .Select(p => p.UserId).ToListAsync()).ToHashSet();

        // Trạng thái khóa tài khoản (Identity lockout) + lý do (lưu ở claim)
        var lockedUserIds = users
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow)
            .Select(u => u.Id).ToHashSet();
        var lockReasons = new Dictionary<string, string>();
        foreach (var u in users.Where(u => lockedUserIds.Contains(u.Id)))
        {
            var reason = (await _userManager.GetClaimsAsync(u)).FirstOrDefault(c => c.Type == "LockReason")?.Value;
            if (reason != null) lockReasons[u.Id] = reason;
        }

        ViewBag.UserRoles   = userRoles;
        ViewBag.PlaceCounts = placeCounts;
        ViewBag.ProUserIds   = proUserIds;
        ViewBag.AdminUserIds = adminUserIds;
        ViewBag.LockedUserIds = lockedUserIds;
        ViewBag.LockReasons   = lockReasons;
        ViewBag.Q           = q;
        // KPI tổng quan (theo toàn bộ, không theo tìm kiếm)
        ViewBag.TotalUsers  = await _userManager.Users.CountAsync();
        ViewBag.ProCount    = proUserIds.Count;
        ViewBag.AdminCount  = adminUserIds.Count;
        ViewBag.LockedCount = await _context.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);
        return View(users);
    }

    // ── Khóa / mở khóa tài khoản (kèm lý do) ───────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LockUser(string userId, string? reason)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == currentUserId)
        {
            TempData["Error"] = "Không thể khóa tài khoản của chính mình.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        // Lưu lý do qua claim (không cần thêm cột DB)
        foreach (var c in (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "LockReason"))
            await _userManager.RemoveClaimAsync(user, c);
        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim(
            "LockReason", string.IsNullOrWhiteSpace(reason) ? "Vi phạm điều khoản sử dụng." : reason.Trim()));

        // Đẩy user ra khỏi phiên hiện tại (cookie hết hiệu lực trong ~1 phút)
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Success"] = $"Đã khóa tài khoản {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);
        foreach (var c in (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "LockReason"))
            await _userManager.RemoveClaimAsync(user, c);

        TempData["Success"] = $"Đã mở khóa tài khoản {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string userId, string role)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var current = await _userManager.GetRolesAsync(user);
        // Never strip Admin from yourself
        var toRemove = userId == currentUserId
            ? current.Where(r => r != "Admin").ToList()
            : current.ToList();

        await _userManager.RemoveFromRolesAsync(user, toRemove);

        if (role == "Pro")   await _userManager.AddToRoleAsync(user, "Pro");
        if (role == "Admin") await _userManager.AddToRoleAsync(user, "Admin");

        // QUAN TRỌNG: đồng bộ UserProfile.Tier với role.
        // App xác định Pro qua profile.IsPro (Tier=="pro") ở nhiều chỗ — nếu chỉ đổi role
        // mà không đổi Tier thì "Bỏ Pro" không có tác dụng (user vẫn được coi là Pro).
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            _context.UserProfiles.Add(profile);
        }
        if (role == "Admin")
        {
            profile.Tier = "admin";
            profile.ProExpiresAt = null;
            profile.MaxPlaces = int.MaxValue;
            profile.MaxPlans  = int.MaxValue;
        }
        else if (role == "Pro")
        {
            profile.Tier = "pro";
            // Cấp Pro tay: cho 30 ngày nếu chưa có hạn xa hơn
            if (profile.ProExpiresAt == null || profile.ProExpiresAt < DateTime.UtcNow.AddDays(30))
                profile.ProExpiresAt = DateTime.UtcNow.AddDays(30);
            profile.MaxPlaces = int.MaxValue;
            profile.MaxPlans  = int.MaxValue;
        }
        else // "User" = bỏ Pro
        {
            profile.Tier = "free";
            profile.ProExpiresAt = null;
            profile.MaxPlaces = 3;
            profile.MaxPlans  = 3;
        }
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // KHÔNG đổi security stamp (sẽ ép user đăng xuất). Cookie tự làm mới claims trong ~1 phút
        // (SecurityStampValidatorOptions.ValidationInterval) nên role mới có hiệu lực mà không bị logout.

        TempData["Success"] = $"Đã cập nhật vai trò cho {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string userId)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == currentUserId)
        {
            TempData["Error"] = "Không thể xóa tài khoản của chính mình.";
            return RedirectToAction(nameof(Users));
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            await _userManager.DeleteAsync(user);
            TempData["Success"] = $"Đã xóa tài khoản {user.Email}.";
        }
        return RedirectToAction(nameof(Users));
    }

    // ── Doanh thu ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Revenue()
    {
        // Tất cả giao dịch (mọi trạng thái) — để bảng lịch sử hiện ĐỦ mọi người
        var all = await _context.Payments
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new RevenueRow(
                p.Id, p.Amount, p.PlanType, p.Code, p.Provider, p.PaidAt, p.Status,
                _context.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync();
        // Doanh thu & biểu đồ chỉ cộng đơn đã thanh toán
        var paid = all.Where(p => p.Status == "paid").ToList();

        const int VN = 7;                               // Việt Nam = UTC+7 (không DST)
        var vnNow = DateTime.UtcNow.AddHours(VN);
        var vnMonthStart = new DateTime(vnNow.Year, vnNow.Month, 1);
        ViewBag.Total      = paid.Sum(p => p.Amount);
        ViewBag.Count      = paid.Count;
        ViewBag.MonthTotal = paid.Where(p => p.PaidAt.HasValue && p.PaidAt.Value.AddHours(VN) >= vnMonthStart).Sum(p => p.Amount);
        ViewBag.MonthCount = paid.Count(p => p.PaidAt.HasValue && p.PaidAt.Value.AddHours(VN) >= vnMonthStart);

        // Doanh thu theo TUẦN (12 tuần gần nhất, tuần bắt đầu thứ 2, giờ VN +7)
        var vnToday = vnNow.Date;
        var weekStart = vnToday.AddDays(-(((int)vnToday.DayOfWeek + 6) % 7));
        var weeks = Enumerable.Range(0, 12).Select(i => weekStart.AddDays(-7 * (11 - i))).ToList();
        ViewBag.TrendLabels = System.Text.Json.JsonSerializer.Serialize(weeks.Select(w => w.ToString("dd/MM")).ToList());
        ViewBag.TrendData   = System.Text.Json.JsonSerializer.Serialize(
            weeks.Select(w => paid.Where(p => p.PaidAt.HasValue && p.PaidAt.Value.AddHours(VN).Date >= w && p.PaidAt.Value.AddHours(VN).Date < w.AddDays(7)).Sum(p => p.Amount)).ToList());

        // Cơ cấu theo gói (cho biểu đồ tròn)
        var byPlan = paid.GroupBy(p => p.PlanType).OrderByDescending(g => g.Sum(x => x.Amount)).ToList();
        ViewBag.ChannelLabels = System.Text.Json.JsonSerializer.Serialize(
            byPlan.Select(g => g.Key == "year" ? "Gói năm" : g.Key == "month" ? "Gói tháng" : (g.Key ?? "Khác")).ToList());
        ViewBag.ChannelData   = System.Text.Json.JsonSerializer.Serialize(byPlan.Select(g => g.Sum(x => x.Amount)).ToList());
        // Số giao dịch thử nghiệm (giá cũ < 59.000) để hiện nút dọn dẹp
        ViewBag.TestCount = await _context.Payments.CountAsync(p => p.Amount < 59000m);
        // Số đơn Pro mẫu (dấu ẩn TransactionId "SEED-") để hiện nút xoá bớt
        ViewBag.SeedCount = await _context.Payments.CountAsync(p => p.TransactionId != null && p.TransactionId.StartsWith("SEED-"));
        return View(all);
    }

    // Dọn các giao dịch thử nghiệm (giá cũ < 59.000đ — mấy cái 5.000 test) để bắt đầu giá mới
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearTestPayments()
    {
        var test = await _context.Payments.Where(p => p.Amount < 59000m).ToListAsync();
        _context.Payments.RemoveRange(test);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa {test.Count} giao dịch thử nghiệm (giá cũ < 59.000đ).";
        return RedirectToAction(nameof(Revenue));
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    // Lưu ảnh thành base64 data URL trong DB (bền với Render redeploy, không dùng /uploads ephemeral)
    private async Task<string> SaveUploadAsync(IFormFile file, string folder)
        => await ImageHelper.ToDataUrlAsync(file) ?? string.Empty;

    // Tạo slug thân thiện từ tiêu đề (bỏ dấu tiếng Việt)
    private static string MakeSlug(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Guid.NewGuid().ToString("n")[..8];
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var t = sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant().Replace('đ', 'd');
        t = System.Text.RegularExpressions.Regex.Replace(t, "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(t) ? Guid.NewGuid().ToString("n")[..8] : t;
    }

    // Thêm nhiều ảnh (thường + menu + video) cho địa điểm, lưu base64. Ảnh thường đầu tiên thành ảnh đại diện nếu chưa có.
    private async Task AddPlaceImagesAsync(int placeId, IFormFile[]? moreImages, IFormFile[]? menuImages, string? userId, IFormFile? videoFile = null)
    {
        foreach (var f in moreImages ?? Array.Empty<IFormFile>())
        {
            var url = await ImageHelper.ToDataUrlAsync(f);
            if (url == null) continue;
            var hasPrimary = await _context.PlaceImages.AnyAsync(i => i.PlaceId == placeId && i.IsPrimary);
            _context.PlaceImages.Add(new PlaceImage { PlaceId = placeId, Url = url, IsPrimary = !hasPrimary, IsMenu = false, UploadedByUserId = userId });
            await _context.SaveChangesAsync();
        }
        foreach (var f in menuImages ?? Array.Empty<IFormFile>())
        {
            var url = await ImageHelper.ToDataUrlAsync(f);
            if (url == null) continue;
            _context.PlaceImages.Add(new PlaceImage { PlaceId = placeId, Url = url, IsPrimary = false, IsMenu = true, UploadedByUserId = userId });
        }
        if (videoFile != null && await _context.PlaceImages.CountAsync(i => i.PlaceId == placeId && i.IsVideo) < 3)
        {
            var vurl = await ImageHelper.VideoToDataUrlAsync(videoFile);
            if (vurl != null)
                _context.PlaceImages.Add(new PlaceImage { PlaceId = placeId, Url = vurl, IsPrimary = false, IsMenu = false, IsVideo = true, UploadedByUserId = userId });
        }
        await _context.SaveChangesAsync();
    }
}

public record RevenueRow(int Id, decimal Amount, string PlanType, string? Code, string? Provider, DateTime? PaidAt, string? Status, string? Email);
