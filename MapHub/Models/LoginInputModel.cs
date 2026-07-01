using System.ComponentModel.DataAnnotations;

namespace MapHub.Models;

public class LoginInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [Display(Name = "Tên đăng nhập")]
    public string Email { get; set; } = string.Empty;   // giữ tên thuộc tính: nhận tên đăng nhập HOẶC email

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    public string ReturnUrl { get; set; } = "/";
}
