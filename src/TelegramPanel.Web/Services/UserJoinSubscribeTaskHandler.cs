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
        if (config == null || config.AccountIds == null || config.Links == null)
        {
            _logger.LogWarning("Invalid config for task {TaskId}", host.TaskId);
            await host.UpdateProgressAsync(0, host.Total, cancellationToken);
            return;
        }

        var accountTools = host.Services.GetRequiredService<AccountTelegramToolsService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();

        var completed = 0;
        var failed = 0;

        foreach (var accountId in config.AccountIds)
        {
            if (!await host.IsStillRunningAsync(cancellationToken))
                break;

            // 检查账号是否存在
            var account = await accountManagement.GetAccountAsync(accountId);
            if (account == null)
            {
                _logger.LogWarning("Account {AccountId} not found, skipping", accountId);
                failed += config.Links.Count;
                await host.UpdateProgressAsync(completed, failed, cancellationToken);
                continue;
            }

            foreach (var link in config.Links)
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

        _logger.LogInformation("Task {TaskId} completed: {Completed} succeeded, {Failed} failed", 
            host.TaskId, completed, failed);
    }

    private class UserJoinSubscribeConfig
    {
        public List<int>? AccountIds { get; set; }
        public List<string>? Links { get; set; }
        public int DelayMs { get; set; }
    }
}

