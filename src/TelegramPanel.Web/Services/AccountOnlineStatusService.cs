using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TL;

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

        // 获取更新间隔（秒）- 改为秒级控制，默认30秒以更频繁地保持在线
        var intervalSeconds = _configuration.GetValue("OnlineStatus:UpdateIntervalSeconds", 30);
        if (intervalSeconds < 20) intervalSeconds = 20;  // 最小20秒
        if (intervalSeconds > 600) intervalSeconds = 600;

        // 延迟启动，等待应用初始化完成
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        _logger.LogInformation("账号在线状态维护服务已启动，更新间隔：{Interval} 秒", intervalSeconds);

        // 首次运行时初始化所有活跃账号的连接
        try
        {
            await InitializeActiveAccountConnectionsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "初始化账号连接时出错");
        }

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
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }

        _logger.LogInformation("账号在线状态维护服务已停止");
    }

    /// <summary>
    /// 初始化所有活跃账号的连接
    /// </summary>
    private async Task InitializeActiveAccountConnectionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var accountManagement = scope.ServiceProvider.GetRequiredService<AccountManagementService>();

        var accounts = await accountManagement.GetAllAccountsAsync();
        var activeAccounts = accounts.Where(a => a.IsActive && !string.IsNullOrWhiteSpace(a.SessionPath)).ToList();

        if (activeAccounts.Count == 0)
        {
            _logger.LogInformation("没有需要初始化的活跃账号");
            return;
        }

        _logger.LogInformation("开始初始化 {Count} 个活跃账号的连接", activeAccounts.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var account in activeAccounts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // 尝试获取或创建客户端连接
                var apiId = int.TryParse(_configuration["Telegram:ApiId"], out var globalApiId) && globalApiId > 0
                    ? globalApiId
                    : (account.ApiId > 0 ? account.ApiId : 0);

                var apiHash = !string.IsNullOrWhiteSpace(_configuration["Telegram:ApiHash"])
                    ? _configuration["Telegram:ApiHash"]!.Trim()
                    : (!string.IsNullOrWhiteSpace(account.ApiHash) ? account.ApiHash.Trim() : null);

                if (apiId <= 0 || string.IsNullOrWhiteSpace(apiHash))
                {
                    _logger.LogWarning("账号 {AccountId} ({Phone}) 缺少 ApiId/ApiHash，跳过初始化", 
                        account.Id, account.DisplayPhone);
                    continue;
                }

                var sessionKey = !string.IsNullOrWhiteSpace(account.ApiHash) ? account.ApiHash.Trim() : apiHash;
                var absoluteSessionPath = Path.GetFullPath(account.SessionPath);

                if (!File.Exists(absoluteSessionPath))
                {
                    _logger.LogWarning("账号 {AccountId} ({Phone}) 的 session 文件不存在：{Path}", 
                        account.Id, account.DisplayPhone, absoluteSessionPath);
                    continue;
                }

                var client = await _clientPool.GetOrCreateClientAsync(
                    accountId: account.Id,
                    apiId: apiId,
                    apiHash: apiHash,
                    sessionPath: account.SessionPath,
                    sessionKey: sessionKey,
                    phoneNumber: account.Phone,
                    userId: account.UserId > 0 ? account.UserId : null);

                // 确保客户端完全连接
                await client.ConnectAsync();
                if (client.User == null && (client.UserId != 0 || account.UserId != 0))
                {
                    await client.LoginUserIfNeeded(reloginOnFailedResume: false);
                }

                if (client?.User != null)
                {
                    // 1. 主动设置在线状态
                    await client.Account_UpdateStatus(offline: false);
                    
                    // 2. 触发 Updates 接收 - 保持连接活跃
                    await client.Updates_GetState();
                    
                    successCount++;
                    _logger.LogInformation("账号 {AccountId} ({Phone}) 已设置为在线状态", 
                        account.Id, account.DisplayPhone);
                }
                else
                {
                    _logger.LogWarning("账号 {AccountId} ({Phone}) 客户端连接失败：User 为 null", 
                        account.Id, account.DisplayPhone);
                }

                // 避免频繁请求
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                failCount++;
                _logger.LogWarning(ex, "初始化账号 {AccountId} ({Phone}) 连接失败", 
                    account.Id, account.DisplayPhone);
            }
        }

        _logger.LogInformation("账号连接初始化完成：成功 {Success}，失败 {Fail}", successCount, failCount);
    }

    private async Task UpdateOnlineStatusForAllAccountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var accountManagement = scope.ServiceProvider.GetRequiredService<AccountManagementService>();

        // 获取所有活跃账号
        var accounts = await accountManagement.GetAllAccountsAsync();
        var activeAccounts = accounts.Where(a => a.IsActive && !string.IsNullOrWhiteSpace(a.SessionPath)).ToList();

        if (activeAccounts.Count == 0)
        {
            _logger.LogDebug("没有活跃账号需要维护在线状态");
            return;
        }

        var successCount = 0;
        var failCount = 0;
        var skippedCount = 0;

        foreach (var account in activeAccounts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // 先检查客户端是否已存在并已连接
                var client = _clientPool.GetClient(account.Id);
                if (client != null && client.User != null)
                {
                    // 客户端已连接，直接维护在线状态
                    // 1. 主动发送在线状态
                    await client.Account_UpdateStatus(offline: false);
                    
                    // 2. 保持 Updates 流活跃
                    await client.Updates_GetState();
                    
                    successCount++;
                    _logger.LogTrace("账号 {AccountId} ({Phone}) 连接保持活跃", 
                        account.Id, account.DisplayPhone);
                }
                else
                {
                    // 客户端不存在或未连接，跳过
                    // （初始化会在 InitializeActiveAccountConnectionsAsync 中处理）
                    skippedCount++;
                }

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
            _logger.LogInformation("在线状态维护：保持活跃 {Success}，跳过 {Skipped}，失败 {Fail}", 
                successCount, skippedCount, failCount);
        }
    }
}

