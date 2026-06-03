namespace MapHub.Services;

// Background service: định kỳ hạ các tài khoản Pro hết hạn về Free
public class ProExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProExpiryService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public ProExpiryService(IServiceScopeFactory scopeFactory, ILogger<ProExpiryService> logger)
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
                var proService = scope.ServiceProvider.GetRequiredService<IProService>();
                var count = await proService.ExpireOverdueAsync();
                if (count > 0)
                    _logger.LogInformation("Đã hạ {Count} tài khoản Pro hết hạn về Free.", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kiểm tra Pro hết hạn.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
