using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
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

        // 获取更新间隔（秒）- 改为秒级控制，默认90秒
        var intervalSeconds = _configuration.GetValue("OnlineStatus:UpdateIntervalSeconds", 90);
        if (intervalSeconds < 30) intervalSeconds = 30;
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
                    _logger.LogDebug("账号 {AccountId} ({Phone}) 缺少 ApiId/ApiHash，跳过初始化", 
                        account.Id, account.DisplayPhone);
                    continue;
                }

                var sessionKey = !string.IsNullOrWhiteSpace(account.ApiHash) ? account.ApiHash.Trim() : apiHash;
                var absoluteSessionPath = Path.GetFullPath(account.SessionPath);

                if (!File.Exists(absoluteSessionPath))
                {
                    _logger.LogDebug("账号 {AccountId} ({Phone}) 的 session 文件不存在，跳过初始化", 
                        account.Id, account.DisplayPhone);
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

                if (client?.User != null)
                {
                    // 执行一次轻量级操作确认连接并触发 Updates 流
                    await client.Users_GetUsers(InputUser.Self);
                    
                    // 触发 Updates 接收（对于 WTelegram，这会让客户端开始接收 Updates）
                    // 这是显示在线状态的关键
                    await client.Updates_GetState();
                    
                    successCount++;
                    _logger.LogInformation("账号 {AccountId} ({Phone}) 连接已建立并开始接收 Updates", 
                        account.Id, account.DisplayPhone);
                }

                // 避免频繁请求
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                failCount++;
                _logger.LogDebug(ex, "初始化账号 {AccountId} ({Phone}) 连接失败", 
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
        var activeAccounts = accounts.Where(a => a.IsActive).ToList();

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
                // 检查客户端是否已连接
                var client = _clientPool.GetClient(account.Id);
                if (client == null || client.User == null)
                {
                    // 如果客户端不存在或未登录，跳过
                    skippedCount++;
                    continue;
                }

                // 通过执行轻量级操作保持连接活跃
                // 定期调用 Updates_GetState 可以确保客户端持续接收 Updates
                // 这是保持在线状态的关键
                await client.Updates_GetState();
                successCount++;

                _logger.LogTrace("账号 {AccountId} ({Phone}) 连接保持活跃", 
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
            _logger.LogInformation("在线状态维护：保持活跃 {Success}，跳过 {Skipped}，失败 {Fail}", 
                successCount, skippedCount, failCount);
        }
    }
}

