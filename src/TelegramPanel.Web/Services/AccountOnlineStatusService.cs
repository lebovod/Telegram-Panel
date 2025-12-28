using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 账号在线状态维护服务
/// 定期更新账号的在线状态，保持连接活跃
/// </summary>
public class AccountOnlineStatusService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountOnlineStatusService> _logger;
    private readonly ITelegramClientPool _clientPool;
    private readonly IConfiguration _configuration;

    public AccountOnlineStatusService(
        IServiceScopeFactory scopeFactory,
        ITelegramClientPool clientPool,
        IConfiguration configuration,
        ILogger<AccountOnlineStatusService> logger)
    {
        _scopeFactory = scopeFactory;
        _clientPool = clientPool;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 检查是否启用在线状态维护
        var enabled = _configuration.GetValue("OnlineStatus:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("账号在线状态维护服务已禁用（OnlineStatus:Enabled=false）");
            return;
        }

        // 获取更新间隔（分钟）
        var intervalMinutes = _configuration.GetValue("OnlineStatus:UpdateIntervalMinutes", 5);
        if (intervalMinutes < 1) intervalMinutes = 1;
        if (intervalMinutes > 60) intervalMinutes = 60;

        // 延迟启动，等待应用初始化完成
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _logger.LogInformation("账号在线状态维护服务已启动，更新间隔：{Interval} 分钟", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateOnlineStatusForAllAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新在线状态时出错");
            }

            // 根据配置的间隔更新
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }

        _logger.LogInformation("账号在线状态维护服务已停止");
    }

    private async Task UpdateOnlineStatusForAllAccountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var accountManagement = scope.ServiceProvider.GetRequiredService<AccountManagementService>();

        // 获取所有活跃账号
        var accounts = await accountManagement.GetAllAccountsAsync();
        var activeAccounts = accounts.Where(a => a.IsActive).ToList();

        if (activeAccounts.Count == 0)
        {
            _logger.LogDebug("没有活跃账号需要维护在线状态");
            return;
        }

        _logger.LogInformation("开始更新 {Count} 个账号的在线状态", activeAccounts.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var account in activeAccounts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // 检查客户端是否已连接
                var client = _clientPool.GetClient(account.Id);
                if (client == null || client.User == null)
                {
                    // 如果客户端不存在或未登录，跳过
                    continue;
                }

                // 发送在线状态更新
                await client.Account_UpdateStatus(offline: false);
                successCount++;

                _logger.LogTrace("账号 {AccountId} ({Phone}) 在线状态已更新", 
                    account.Id, account.DisplayPhone);

                // 避免频繁请求，添加小延迟
                await Task.Delay(100, cancellationToken);
            }
            catch (Exception ex)
            {
                failCount++;
                _logger.LogDebug(ex, "更新账号 {AccountId} ({Phone}) 在线状态失败", 
                    account.Id, account.DisplayPhone);
            }
        }

        if (successCount > 0 || failCount > 0)
        {
            _logger.LogInformation("在线状态更新完成：成功 {Success}，失败 {Fail}", 
                successCount, failCount);
        }
    }
}

