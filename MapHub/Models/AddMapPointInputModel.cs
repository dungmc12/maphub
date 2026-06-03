using System.ComponentModel.DataAnnotations;

namespace MapHub.Models;

public class AddMapPointInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên địa điểm.")]
    [StringLength(120, ErrorMessage = "Tên địa điểm tối đa 120 ký tự.")]
    [Display(Name = "Tên địa điểm")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập danh mục.")]
    [StringLength(80, ErrorMessage = "Danh mục tối đa 80 ký tự.")]
    [Display(Name = "Danh mục")]
    public string Category { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    [Display(Name = "Mô tả")]
    public string Description { get; set; } = string.Empty;

    [Range(-90d, 90d, ErrorMessage = "Vĩ độ phải trong khoảng -90 đến 90.")]
    [Display(Name = "Vĩ độ")]
    public double Latitude { get; set; }

    [Range(-180d, 180d, ErrorMessage = "Kinh độ phải trong khoảng -180 đến 180.")]
    [Display(Name = "Kinh độ")]
    public double Longitude { get; set; }
}
