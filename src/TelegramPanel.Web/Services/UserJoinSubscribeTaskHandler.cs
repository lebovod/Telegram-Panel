using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 处理用户加群/订阅任务
/// </summary>
public class UserJoinSubscribeTaskHandler : IModuleTaskHandler
{
    private readonly ILogger<UserJoinSubscribeTaskHandler> _logger;

    public UserJoinSubscribeTaskHandler(ILogger<UserJoinSubscribeTaskHandler> logger)
    {
        _logger = logger;
    }

    public string TaskType => BatchTaskTypes.UserJoinSubscribe;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<UserJoinSubscribeConfig>(host.Config ?? "{}");
        if (config == null || config.AccountIds == null)
        {
            _logger.LogWarning("Invalid config for task {TaskId}", host.TaskId);
            await host.UpdateProgressAsync(0, host.Total, cancellationToken);
            return;
        }

        var accountTools = host.Services.GetRequiredService<AccountTelegramToolsService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();

        // 判断是搜索模式还是直接订阅模式
        var isSearchMode = !string.IsNullOrWhiteSpace(config.SearchUsername);

        if (isSearchMode)
        {
            await ExecuteSearchModeAsync(config, accountTools, accountManagement, host, cancellationToken);
        }
        else if (config.Links != null && config.Links.Count > 0)
        {
            await ExecuteDirectModeAsync(config, accountTools, accountManagement, host, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Invalid config: neither SearchUsername nor Links provided for task {TaskId}", host.TaskId);
            await host.UpdateProgressAsync(0, host.Total, cancellationToken);
        }
    }

    /// <summary>
    /// 搜索模式：每个账号搜索用户名并订阅找到的结果
    /// </summary>
    private async Task ExecuteSearchModeAsync(
        UserJoinSubscribeConfig config,
        AccountTelegramToolsService accountTools,
        AccountManagementService accountManagement,
        IModuleTaskExecutionHost host,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var failed = 0;

        _logger.LogInformation("Task {TaskId} running in SEARCH mode for username: @{Username}", 
            host.TaskId, config.SearchUsername);

        foreach (var accountId in config.AccountIds!)
        {
            if (!await host.IsStillRunningAsync(cancellationToken))
                break;

            var account = await accountManagement.GetAccountAsync(accountId);
            if (account == null)
            {
                _logger.LogWarning("Account {AccountId} not found, skipping", accountId);
                failed++;
                await host.UpdateProgressAsync(completed, failed, cancellationToken);
                continue;
            }

            try
            {
                _logger.LogInformation("Account {AccountId} searching for @{Username}", 
                    accountId, config.SearchUsername);

                // 搜索用户名
                var (searchSuccess, searchError, results) = await accountTools.SearchGlobalAsync(
                    accountId,
                    config.SearchUsername!,
                    limit: 10,
                    cancellationToken);

                if (!searchSuccess || results == null || results.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("Account {AccountId} failed to search @{Username}: {Error}", 
                        accountId, config.SearchUsername, searchError ?? "No results found");
                    await host.UpdateProgressAsync(completed, failed, cancellationToken);
                    
                    // 延迟后继续下一个账号
                    if (config.DelayMs > 0)
                    {
                        var jitter = Random.Shared.Next(500, 1500);
                        await Task.Delay(config.DelayMs + jitter, cancellationToken);
                    }
                    continue;
                }

                // 找到第一个结果并尝试订阅
                var target = results.First();
                var subscribeLink = target.SubscribeIdentifier;

                _logger.LogInformation("Account {AccountId} found {DisplayName}, attempting to subscribe...", 
                    accountId, target.DisplayName);

                var (joinSuccess, joinError, title) = await accountTools.JoinChatOrChannelAsync(
                    accountId,
                    subscribeLink,
                    cancellationToken);

                if (joinSuccess)
                {
                    completed++;
                    _logger.LogInformation("Account {AccountId} successfully subscribed to {Title}", 
                        accountId, title ?? target.DisplayName);
                }
                else
                {
                    failed++;
                    _logger.LogWarning("Account {AccountId} failed to subscribe to {DisplayName}: {Error}", 
                        accountId, target.DisplayName, joinError);
                }

                await host.UpdateProgressAsync(completed, failed, cancellationToken);

                // 延迟，避免触发风控
                if (config.DelayMs > 0)
                {
                    var jitter = Random.Shared.Next(500, 1500);
                    await Task.Delay(config.DelayMs + jitter, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error processing account {AccountId} for search @{Username}", 
                    accountId, config.SearchUsername);
                await host.UpdateProgressAsync(completed, failed, cancellationToken);
            }
        }

        _logger.LogInformation("Task {TaskId} completed (SEARCH mode): {Completed} succeeded, {Failed} failed", 
            host.TaskId, completed, failed);
    }

    /// <summary>
    /// 直接模式：使用提供的链接列表订阅
    /// </summary>
    private async Task ExecuteDirectModeAsync(
        UserJoinSubscribeConfig config,
        AccountTelegramToolsService accountTools,
        AccountManagementService accountManagement,
        IModuleTaskExecutionHost host,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var failed = 0;

        _logger.LogInformation("Task {TaskId} running in DIRECT mode with {Count} links", 
            host.TaskId, config.Links!.Count);

        foreach (var accountId in config.AccountIds!)
        {
            if (!await host.IsStillRunningAsync(cancellationToken))
                break;

            var account = await accountManagement.GetAccountAsync(accountId);
            if (account == null)
            {
                _logger.LogWarning("Account {AccountId} not found, skipping", accountId);
                failed += config.Links!.Count;
                await host.UpdateProgressAsync(completed, failed, cancellationToken);
                continue;
            }

            foreach (var link in config.Links!)
            {
                if (!await host.IsStillRunningAsync(cancellationToken))
                    break;

                try
                {
                    _logger.LogInformation("Account {AccountId} joining {Link}", accountId, link);
                    
                    var (success, error, title) = await accountTools.JoinChatOrChannelAsync(
                        accountId, 
                        link, 
                        cancellationToken);

                    if (success)
                    {
                        completed++;
                        _logger.LogInformation("Account {AccountId} successfully joined {Link} ({Title})", 
                            accountId, link, title);
                    }
                    else
                    {
                        failed++;
                        _logger.LogWarning("Account {AccountId} failed to join {Link}: {Error}", 
                            accountId, link, error);
                    }

                    await host.UpdateProgressAsync(completed, failed, cancellationToken);

                    // 延迟，避免触发风控
                    if (config.DelayMs > 0)
                    {
                        var jitter = Random.Shared.Next(500, 1500);
                        await Task.Delay(config.DelayMs + jitter, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Error joining {Link} for account {AccountId}", link, accountId);
                    await host.UpdateProgressAsync(completed, failed, cancellationToken);
                }
            }
        }

        _logger.LogInformation("Task {TaskId} completed (DIRECT mode): {Completed} succeeded, {Failed} failed", 
            host.TaskId, completed, failed);
    }

    private class UserJoinSubscribeConfig
    {
        public List<int>? AccountIds { get; set; }
        public List<string>? Links { get; set; }
        public string? SearchUsername { get; set; }
        public int DelayMs { get; set; }
    }
}

