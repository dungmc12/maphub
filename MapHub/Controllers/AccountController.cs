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

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
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
