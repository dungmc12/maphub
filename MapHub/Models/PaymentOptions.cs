namespace MapHub.Models;

// Cấu hình nhận tiền qua SePay + VietQR (đọc từ appsettings.json "SePay")
public class SePayOptions
{
    public string ApiKey { get; set; } = "";          // chuỗi Authorization đặt trong webhook SePay
    public string BankCode { get; set; } = "MB";       // mã ngân hàng VietQR: MB, VCB, TCB, ACB...
    public string AccountNumber { get; set; } = "";    // số tài khoản nhận tiền
    public string AccountName { get; set; } = "";      // tên chủ tài khoản (IN HOA, không dấu)
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
