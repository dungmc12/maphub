using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IAppEmailSender _emailSender;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IAppEmailSender emailSender,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Map");
        }

        return View(new LoginInputModel
        {
            ReturnUrl = NormalizeReturnUrl(returnUrl)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel model, string? returnUrl = null)
    {
        var targetUrl = NormalizeReturnUrl(returnUrl ?? model.ReturnUrl);
        model.ReturnUrl = targetUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Cho đăng nhập bằng TÊN ĐĂNG NHẬP hoặc EMAIL: nếu nhập email thì tra ra tên đăng nhập tương ứng
        var loginId = (model.Email ?? "").Trim();
        if (loginId.Contains('@'))
        {
            var byEmail = await _userManager.FindByEmailAsync(loginId);
            if (byEmail != null) loginId = byEmail.UserName!;
        }

        var result = await _signInManager.PasswordSignInAsync(
            loginId,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
            var u = await _userManager.FindByNameAsync(loginId);
            if (u != null) await ExpireTrialIfNeededAsync(u);
            return LocalRedirect(targetUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, await LockedMessageAsync(model.Email));
            return View(model);
        }

        // Đúng mật khẩu nhưng CHƯA xác thực email → hướng dẫn + cho gửi lại link
        if (result.IsNotAllowed)
        {
            var pending = await _userManager.FindByNameAsync(loginId);
            if (pending != null && !pending.EmailConfirmed)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản chưa xác thực email. Kiểm tra hộp thư (kể cả Spam) hoặc bấm gửi lại bên dưới.");
                ViewBag.ShowResend = pending.Email;
                return View(model);
            }
        }

        ModelState.AddModelError(string.Empty, "Sai email hoặc mật khẩu.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Map");
        }

        return View(new RegisterInputModel
        {
            ReturnUrl = NormalizeReturnUrl(returnUrl)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInputModel model, string? returnUrl = null)
    {
        var targetUrl = NormalizeReturnUrl(returnUrl ?? model.ReturnUrl);
        model.ReturnUrl = targetUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim().ToLowerInvariant();

        // Email đã có tài khoản?
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (existing.EmailConfirmed)
            {
                ModelState.AddModelError(nameof(model.Email), "Email này đã có tài khoản. Hãy đăng nhập (hoặc dùng Quên mật khẩu).");
                return View(model);
            }
            // Tài khoản cũ CHƯA xác thực (đăng ký dở/mail lỗi) → xoá để đăng ký lại từ đầu.
            // An toàn: chưa kích hoạt thì chưa có dữ liệu, và người giữ hộp mail mới là chủ thật.
            await _userManager.DeleteAsync(existing);
        }

        // Tạo tài khoản CHƯA xác thực — phải bấm link trong email mới đăng nhập được
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        // SMTP chưa cấu hình (chưa đặt Smtp__User/Smtp__Pass) → không thể gửi mail:
        // tự xác nhận để không chặn người dùng, và ghi log nhắc cấu hình.
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning("SMTP chưa cấu hình — tự xác nhận email cho {Email}. Đặt env Smtp__User/Smtp__Pass để bật xác thực thật.", email);
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
            var trialNow = await GrantTrialAsync(user, email.Split('@')[0]);
            await _signInManager.SignInAsync(user, isPersistent: false);
            if (trialNow.HasValue)
                TempData["TrialMsg"] = $"🎉 Chào mừng! Bạn được dùng thử Pro miễn phí đến hết ngày {trialNow.Value.ToLocalTime():dd/MM/yyyy}.";
            return LocalRedirect(targetUrl);
        }

        var sent = await SendConfirmationEmailAsync(user, targetUrl);
        if (!sent)
        {
            // Gửi thất bại (SMTP lỗi) → xoá tài khoản vừa tạo để user đăng ký lại được, báo lỗi rõ
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Không gửi được email xác thực lúc này. Vui lòng thử lại sau ít phút.");
            return View(model);
        }

        return View("RegisterConfirmation", model: email);
    }

    // Gửi email chứa link xác thực tài khoản. Trả về false nếu SMTP lỗi.
    private async Task<bool> SendConfirmationEmailAsync(ApplicationUser user, string? returnUrl = null)
    {
        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = Url.Action(nameof(ConfirmEmail), "Account",
                new { userId = user.Id, token, returnUrl }, protocol: Request.Scheme)!;
            var html = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;border:1px solid #e2e8f0;border-radius:14px;overflow:hidden'>
  <div style='background:#0891B2;color:#fff;padding:18px 24px;font-size:18px;font-weight:700'>🌏 CityScout</div>
  <div style='padding:24px'>
    <h2 style='margin:0 0 10px;font-size:19px;color:#0F172A'>Xác thực email của bạn</h2>
    <p style='color:#475569;font-size:14px;line-height:1.6'>
      Cảm ơn bạn đã đăng ký CityScout! Bấm nút bên dưới để xác thực email và kích hoạt tài khoản
      (kèm <strong>7 ngày dùng thử Pro miễn phí</strong>).
    </p>
    <p style='text-align:center;margin:26px 0'>
      <a href='{link}' style='background:#0891B2;color:#fff;text-decoration:none;padding:12px 28px;border-radius:10px;font-weight:700;font-size:15px'>Xác thực email</a>
    </p>
    <p style='color:#94a3b8;font-size:12px;line-height:1.6'>
      Nếu nút không bấm được, sao chép link này vào trình duyệt:<br>
      <a href='{link}' style='color:#0891B2;word-break:break-all'>{link}</a><br><br>
      Nếu bạn không đăng ký CityScout, hãy bỏ qua email này.
    </p>
  </div>
</div>";
            await _emailSender.SendAsync(user.Email!, "CityScout — Xác thực email đăng ký", html);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi email xác thực thất bại cho {Email}", user.Email);
            return false;
        }
    }

    // Người dùng bấm link trong email → xác nhận + cấp dùng thử Pro + đăng nhập luôn
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string userId, string token, string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return RedirectToAction(nameof(Login));
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return RedirectToAction(nameof(Login));

        if (user.EmailConfirmed)
        {
            TempData["LoginError"] = "Email đã được xác thực trước đó — bạn có thể đăng nhập.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            TempData["LoginError"] = "Link xác thực không hợp lệ hoặc đã hết hạn. Hãy đăng nhập để nhận lại email xác thực.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        // 🎁 Xác thực xong mới cấp dùng thử Pro (mỗi tài khoản 1 lần) + đăng nhập luôn
        var trialUntil = await GrantTrialAsync(user, user.Email!.Split('@')[0]);
        await _signInManager.SignInAsync(user, isPersistent: false);
        if (trialUntil.HasValue)
            TempData["TrialMsg"] = $"🎉 Email đã xác thực! Bạn được dùng thử Pro miễn phí đến hết ngày {trialUntil.Value.ToLocalTime():dd/MM/yyyy}.";
        return LocalRedirect(NormalizeReturnUrl(returnUrl));
    }

    // Gửi lại email xác thực (từ trang đăng nhập khi bị chặn vì chưa xác thực)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation(string email, string? returnUrl = null)
    {
        var user = string.IsNullOrWhiteSpace(email) ? null : await _userManager.FindByEmailAsync(email.Trim());
        // Không lộ thông tin tài khoản tồn tại hay không — luôn báo chung chung
        if (user != null && !user.EmailConfirmed && _emailSender.IsConfigured)
            await SendConfirmationEmailAsync(user, returnUrl);
        TempData["LoginError"] = "✉️ Nếu email đã đăng ký và chưa xác thực, hệ thống vừa gửi lại link xác thực — kiểm tra hộp thư (kể cả mục Spam).";
        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    // Số ngày dùng thử Pro khi đăng ký — mỗi tài khoản chỉ 1 lần
    private const int TrialDays = 7;

    // Cấp dùng thử Pro cho tài khoản mới — CHỈ 1 lần/tài khoản (TrialClaimed). Trả về ngày hết hạn, hoặc null nếu đã dùng.
    private async Task<DateTime?> GrantTrialAsync(ApplicationUser user, string? displayName)
    {
        var profile = await _context.UserProfiles.FindAsync(user.Id);
        if (profile == null)
        {
            profile = new UserProfile { UserId = user.Id };
            _context.UserProfiles.Add(profile);
        }
        if (profile.TrialClaimed) return null;   // đã dùng thử rồi → không cấp lại

        var trialUntil = DateTime.UtcNow.AddDays(TrialDays);
        if (string.IsNullOrWhiteSpace(profile.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
            profile.DisplayName = displayName.Trim();
        profile.Tier = "pro";
        profile.ProExpiresAt = trialUntil;
        profile.MaxPlaces = int.MaxValue;
        profile.MaxPlans = int.MaxValue;
        profile.TrialClaimed = true;
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        if (!await _userManager.IsInRoleAsync(user, "Pro"))
            await _userManager.AddToRoleAsync(user, "Pro");
        return trialUntil;
    }

    // Hết hạn dùng thử/Pro → tự hạ về Free (chạy khi đăng nhập). Admin không bị ảnh hưởng.
    private async Task ExpireTrialIfNeededAsync(ApplicationUser user)
    {
        var p = await _context.UserProfiles.FindAsync(user.Id);
        if (p == null) return;
        if (p.Tier == "pro" && p.ProExpiresAt.HasValue && p.ProExpiresAt.Value <= DateTime.UtcNow)
        {
            p.Tier = "free";
            p.MaxPlaces = 3;
            p.MaxPlans = 3;
            p.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            if (await _userManager.IsInRoleAsync(user, "Pro"))
                await _userManager.RemoveFromRoleAsync(user, "Pro");
        }
    }

    // Đăng nhập bằng Google
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GoogleLogin(string? returnUrl = null)
    {
        // Nếu chưa cấu hình Google OAuth (thiếu ClientId/Secret) thì báo thân thiện, không để crash 500
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        if (schemes.All(s => s.Name != "Google"))
        {
            TempData["LoginError"] = "Đăng nhập Google chưa được cấu hình. Vui lòng dùng email/mật khẩu, hoặc liên hệ quản trị viên.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var redirectUrl = Url.Action(nameof(GoogleCallback), "Account", new { returnUrl });
        var props = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return Challenge(props, "Google");
    }

    [HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Lỗi từ Google: {remoteError}");
            return View("Login");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null) return RedirectToAction(nameof(Login));

        // Tên hiển thị từ Google (để đánh giá hiện đúng tên người dùng, giống Google reviews)
        var googleName = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                         ?? info.Principal.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value;

        // Thử login bằng external account đã liên kết
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
        if (result.Succeeded)
        {
            var linked = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linked != null) { await EnsureDisplayNameAsync(linked, googleName); await ExpireTrialIfNeededAsync(linked); }
            return LocalRedirect(NormalizeReturnUrl(returnUrl));
        }
        if (result.IsLockedOut)
        {
            TempData["LoginError"] = await LockedMessageAsync(info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);
            return RedirectToAction(nameof(Login));
        }

        // Chưa có tài khoản → tự tạo từ thông tin Google
        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
        if (string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "Không lấy được email từ Google.");
            return View("Login");
        }

        var user = await _userManager.FindByEmailAsync(email);
        bool isNewGoogleUser = false;
        if (user == null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Không thể tạo tài khoản.");
                return View("Login");
            }
            isNewGoogleUser = true;
        }

        // Chặn user đã bị khóa (đường vòng tạo/liên kết bỏ qua lockout)
        if (await _userManager.IsLockedOutAsync(user))
        {
            TempData["LoginError"] = await LockedMessageAsync(email);
            return RedirectToAction(nameof(Login));
        }

        await _userManager.AddLoginAsync(user, info);
        await EnsureDisplayNameAsync(user, googleName);

        // Tài khoản Google MỚI cũng được dùng thử Pro 1 lần (giống đăng ký thường)
        if (isNewGoogleUser) await GrantTrialAsync(user, googleName);

        await _signInManager.SignInAsync(user, isPersistent: false);
        if (isNewGoogleUser)
            TempData["TrialMsg"] = $"🎉 Chào mừng! Bạn được dùng thử Pro miễn phí đến hết ngày {DateTime.UtcNow.AddDays(TrialDays).ToLocalTime():dd/MM/yyyy}.";
        return LocalRedirect(NormalizeReturnUrl(returnUrl));
    }

    // Lưu tên hiển thị (từ Google) vào hồ sơ nếu hồ sơ chưa có tên — để đánh giá hiện đúng tên người dùng
    private async Task EnsureDisplayNameAsync(ApplicationUser user, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return;
        var profile = await _context.UserProfiles.FindAsync(user.Id);
        if (profile == null)
        {
            _context.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = displayName.Trim() });
            await _context.SaveChangesAsync();
        }
        else if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            profile.DisplayName = displayName.Trim();
            profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    private async Task<string> LockedMessageAsync(string? email)
    {
        var u = string.IsNullOrWhiteSpace(email) ? null : await _userManager.FindByEmailAsync(email);
        var reason = u != null
            ? (await _userManager.GetClaimsAsync(u)).FirstOrDefault(c => c.Type == "LockReason")?.Value
            : null;
        return $"🔒 Tài khoản đã bị khóa. Lý do: {reason ?? "Vui lòng liên hệ quản trị viên."}";
    }

    // ── Hồ sơ cá nhân ─────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var userId = _userManager.GetUserId(User);
        var user = await _userManager.GetUserAsync(User);
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId! };
            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();
        }
        ViewBag.Email = user?.Email;
        ViewBag.HasPassword = user != null && await _userManager.HasPasswordAsync(user);
        return View(profile);
    }

    // Phục vụ ảnh đại diện theo userId (có cache) → navbar/khắp nơi dùng <img src> nhẹ,
    // không nhồi base64 vào mọi trang. Avatar lưu data:base64 thì giải mã; URL ngoài thì redirect.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Avatar(string id)
    {
        var url = await _context.UserProfiles
            .Where(p => p.UserId == id).Select(p => p.AvatarUrl).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(url)) return NotFound();
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma < 0) return NotFound();
            var mime = url[5..comma].Split(';')[0];
            try
            {
                var bytes = Convert.FromBase64String(url[(comma + 1)..]);
                Response.Headers["Cache-Control"] = "public, max-age=2592000";
                return File(bytes, string.IsNullOrWhiteSpace(mime) ? "image/jpeg" : mime);
            }
            catch { return NotFound(); }
        }
        return Redirect(url);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string? displayName, string? avatarUrl, IFormFile? avatarFile)
    {
        var userId = _userManager.GetUserId(User);
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null) { profile = new UserProfile { UserId = userId! }; _context.UserProfiles.Add(profile); }
        profile.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        // Ưu tiên ảnh tải từ máy → lưu base64 (bền với Render redeploy, không mất ảnh).
        // Không có file thì dùng URL dán vào; cả hai trống thì GIỮ ảnh cũ (không xoá nhầm).
        var dataUrl = await ImageHelper.ToDataUrlAsync(avatarFile);
        if (dataUrl != null)
            profile.AvatarUrl = dataUrl;
        else if (!string.IsNullOrWhiteSpace(avatarUrl))
            profile.AvatarUrl = avatarUrl.Trim();

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["ProfileMsg"] = avatarFile != null && dataUrl == null
            ? "Đã lưu hồ sơ, nhưng ảnh không hợp lệ (chỉ nhận ảnh ≤5MB)."
            : "Đã cập nhật hồ sơ.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string? currentPassword, string newPassword, string confirmPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
        { TempData["PwError"] = "Mật khẩu mới tối thiểu 6 ký tự."; return RedirectToAction(nameof(Profile)); }
        if (newPassword != confirmPassword)
        { TempData["PwError"] = "Mật khẩu xác nhận không khớp."; return RedirectToAction(nameof(Profile)); }

        // User đăng nhập Google có thể chưa có mật khẩu → dùng AddPassword
        IdentityResult result = await _userManager.HasPasswordAsync(user)
            ? await _userManager.ChangePasswordAsync(user, currentPassword ?? "", newPassword)
            : await _userManager.AddPasswordAsync(user, newPassword);

        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData["PwMsg"] = "Đã đổi mật khẩu thành công.";
        }
        else
        {
            TempData["PwError"] = string.Join(" ", result.Errors.Select(e => e.Description));
        }
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Login));
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return Url.Action("Index", "Map") ?? "/";
    }
}
