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

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
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

        var username = model.Username.Trim();

        // Tên đăng nhập đã tồn tại?
        if (await _userManager.FindByNameAsync(username) != null)
        {
            ModelState.AddModelError(nameof(model.Username), "Tên đăng nhập đã tồn tại, vui lòng chọn tên khác.");
            return View(model);
        }

        // Không dùng email thật → sinh email nội bộ để thoả ràng buộc Identity (không cần xác thực email)
        var user = new ApplicationUser
        {
            UserName = username,
            Email = $"{username.ToLowerInvariant()}@cityscout.local",
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            // 🎁 Dùng thử Pro miễn phí — mỗi tài khoản CHỈ 1 lần (đánh dấu TrialClaimed)
            var trialUntil = DateTime.UtcNow.AddDays(TrialDays);
            _context.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                DisplayName = username,
                Tier = "pro",
                ProExpiresAt = trialUntil,
                MaxPlaces = int.MaxValue,
                MaxPlans = int.MaxValue,
                TrialClaimed = true
            });
            await _context.SaveChangesAsync();
            await _userManager.AddToRoleAsync(user, "Pro");

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["TrialMsg"] = $"🎉 Chào mừng {username}! Bạn được dùng thử Pro miễn phí đến hết ngày {trialUntil.ToLocalTime():dd/MM/yyyy}.";
            return LocalRedirect(targetUrl);
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    // Số ngày dùng thử Pro khi đăng ký (30 = 1 tháng; đổi 90 nếu muốn 3 tháng)
    private const int TrialDays = 30;

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
            if (linked != null) await EnsureDisplayNameAsync(linked, googleName);
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
        if (user == null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Không thể tạo tài khoản.");
                return View("Login");
            }
        }

        // Chặn user đã bị khóa (đường vòng tạo/liên kết bỏ qua lockout)
        if (await _userManager.IsLockedOutAsync(user))
        {
            TempData["LoginError"] = await LockedMessageAsync(email);
            return RedirectToAction(nameof(Login));
        }

        await _userManager.AddLoginAsync(user, info);
        await EnsureDisplayNameAsync(user, googleName);
        await _signInManager.SignInAsync(user, isPersistent: false);
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
