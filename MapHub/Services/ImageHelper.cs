namespace MapHub.Services;

// Lưu ảnh upload thành chuỗi base64 data URL để cất thẳng vào DB.
// Lý do: Render free dùng ổ đĩa ephemeral — file trong /uploads bị xóa mỗi lần deploy/restart.
// Cất trong DB thì ảnh tồn tại vĩnh viễn (đánh đổi: phình DB, nên giới hạn dung lượng).
public static class ImageHelper
{
    private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public static async Task<string?> ToDataUrlAsync(IFormFile? file, long maxBytes = 5 * 1024 * 1024)
    {
        if (file == null || file.Length == 0 || file.Length > maxBytes) return null;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) return null;

        var mime = ext switch
        {
            ".png"  => "image/png",
            ".webp" => "image/webp",
            ".gif"  => "image/gif",
            _       => "image/jpeg"
        };

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    private static readonly string[] AllowedVideoExt = { ".mp4", ".webm", ".mov", ".ogg" };

    // Video ngắn → base64 data URL. Giới hạn dung lượng mặc định 15MB (kết hợp giới hạn thời lượng ở client).
    public static async Task<string?> VideoToDataUrlAsync(IFormFile? file, long maxBytes = 15 * 1024 * 1024)
    {
        if (file == null || file.Length == 0 || file.Length > maxBytes) return null;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedVideoExt.Contains(ext)) return null;

        var mime = ext switch
        {
            ".webm" => "video/webm",
            ".mov"  => "video/quicktime",
            ".ogg"  => "video/ogg",
            _       => "video/mp4"
        };

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
    }
}
