using System.ComponentModel.DataAnnotations;

namespace MapHub.Models;

public class LoginInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng (vd: ban@gmail.com).")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    public string ReturnUrl { get; set; } = "/";
}
