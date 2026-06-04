using MapHub.Data;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Services;

// Background service: định kỳ hỏi Casso API và tự lên Pro cho các đơn đã chuyển khoản,
// để KHÔNG cần ai bấm "Kiểm tra ngay" và không phụ thuộc 100% vào webhook (có thể bị trượt).
public class PaymentAutoCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentAutoCheckService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    public PaymentAutoCheckService(IServiceScopeFactory scopeFactory, ILogger<PaymentAutoCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var casso = scope.ServiceProvider.GetRequiredService<ICassoApiService>();
                if (casso.IsConfigured)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var pro = scope.ServiceProvider.GetRequiredService<IProService>();

                    // Đơn còn chờ trong 2 ngày gần đây
                    var cutoff = DateTime.UtcNow.AddDays(-2);
                    var pending = await db.Payments
                        .Where(p => p.Status != "paid" && p.Status != "cancelled" && p.CreatedAt >= cutoff)
                        .ToListAsync(stoppingToken);

                    if (pending.Count > 0)
                    {
                        // Có đơn vừa tạo trong 30 phút (đang thanh toán) → tự bấm "Đồng bộ ngay"
                        // để Casso đọc bank + đẩy webhook, thay vì bạn phải vào Casso bấm tay.
                        if (pending.Any(p => p.CreatedAt >= DateTime.UtcNow.AddMinutes(-30)))
                            await casso.TriggerSyncAsync(stoppingToken);

                        var txs = await casso.GetRecentTransactionsAsync(100, stoppingToken);
                        foreach (var payment in pending)
                        {
                            var wanted = payment.Code?.ToUpperInvariant();
                            if (string.IsNullOrEmpty(wanted)) continue;

                            var match = txs.FirstOrDefault(t =>
                                $"{t.Description} {t.Tid}".ToUpperInvariant().Contains(wanted) &&
                                t.Amount >= payment.Amount);
                            if (match == null) continue;

                            payment.Status = "paid";
                            payment.PaidAt = DateTime.UtcNow;
                            payment.TransactionId = match.Tid;
                            await db.SaveChangesAsync(stoppingToken);
                            await pro.ActivateProAsync(payment.UserId, payment.PlanType);
                            _logger.LogInformation("Auto-check: tự lên Pro cho đơn {Code}", payment.Code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentAutoCheckService lỗi");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
