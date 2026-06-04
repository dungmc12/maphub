namespace MapHub.Models;

public class PayOSOptions
{
    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChecksumKey { get; set; } = "";
}

// Cấu hình nhận tiền qua SePay + VietQR (đọc từ appsettings.json "SePay")
public class SePayOptions
{
    public string ApiKey { get; set; } = "";          // chuỗi Authorization đặt trong webhook SePay
    public string BankCode { get; set; } = "MB";       // mã ngân hàng VietQR: MB, VCB, TCB, ACB...
    public string AccountNumber { get; set; } = "";    // số tài khoản nhận tiền
    public string AccountName { get; set; } = "";      // tên chủ tài khoản (IN HOA, không dấu)
}

// Cấu hình gọi Casso API để chủ động kiểm tra giao dịch (đọc từ appsettings.json "Casso")
public class CassoOptions
{
    public string ApiKey { get; set; } = "";                       // Casso API key (tạo ở trang API Keys)
    public string BaseUrl { get; set; } = "https://oauth.casso.vn"; // gốc API Casso v2
    public string BankAccountId { get; set; } = "";                 // số TK ngân hàng — để gọi /v2/sync (buộc Casso đọc bank)
}

// Cấu hình giá các gói Pro (đọc từ appsettings.json "Pricing")
public class PricingOptions
{
    public PlanPrice Month { get; set; } = new() { Amount = 5000, Days = 30, Label = "1 tháng" };
    public PlanPrice Year { get; set; } = new() { Amount = 990000, Days = 365, Label = "1 năm" };

    public PlanPrice For(string? planType) =>
        string.Equals(planType, "year", StringComparison.OrdinalIgnoreCase) ? Year : Month;
}

public class PlanPrice
{
    public decimal Amount { get; set; }
    public int Days { get; set; }
    public string Label { get; set; } = "";
}
