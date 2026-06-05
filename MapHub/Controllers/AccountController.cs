using MapHub.Data;
using MapHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

        var result = await _signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
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

        // Chỉ cho phép Gmail
        if (!model.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Chỉ chấp nhận đăng ký bằng tài khoản Gmail (@gmail.com).");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(targetUrl);
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
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

        // Thử login bằng external account đã liên kết
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
        if (result.Succeeded)
            return LocalRedirect(NormalizeReturnUrl(returnUrl));
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
        await _signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(NormalizeReturnUrl(returnUrl));
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

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string? displayName, string? avatarUrl)
    {
        var userId = _userManager.GetUserId(User);
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null) { profile = new UserProfile { UserId = userId! }; _context.UserProfiles.Add(profile); }
        profile.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        profile.AvatarUrl   = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();
        profile.UpdatedAt   = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["ProfileMsg"] = "Đã cập nhật hồ sơ.";
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
