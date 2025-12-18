using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using AccountStatus = TelegramPanel.Core.Interfaces.AccountStatus;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// 账号服务实现
/// </summary>
public class AccountService : IAccountService
{
    private readonly ITelegramClientPool _clientPool;
    private readonly ILogger<AccountService> _logger;

    // 临时存储登录状态（实际项目应该使用数据库或缓存）
    private readonly Dictionary<int, string> _pendingLogins = new();

    public AccountService(ITelegramClientPool clientPool, ILogger<AccountService> logger)
    {
        _clientPool = clientPool;
        _logger = logger;
    }

    public async Task<LoginResult> StartLoginAsync(int accountId, string phone)
    {
        // 这里需要从数据库获取 apiId, apiHash, sessionPath
        // 暂时使用示例值
        var apiId = 0; // TODO: 从配置或数据库获取
        var apiHash = ""; // TODO: 从配置或数据库获取
        var sessionPath = $"sessions/{phone}.session";

        var client = await _clientPool.GetOrCreateClientAsync(accountId, apiId, apiHash, sessionPath);

        _logger.LogInformation("Starting login for phone {Phone}", phone);

        var result = await client.Login(phone);

        return result switch
        {
            "verification_code" => new LoginResult(false, "code", "请输入验证码"),
            "password" => new LoginResult(false, "password", "请输入两步验证密码"),
            "name" => new LoginResult(false, "signup", "需要注册新账号"),
            _ when client.User != null => new LoginResult(true, null, "登录成功", MapToAccountInfo(accountId, client)),
            _ => new LoginResult(false, null, $"未知状态: {result}")
        };
    }

    public async Task<LoginResult> SubmitCodeAsync(int accountId, string code)
    {
        var client = _clientPool.GetClient(accountId)
            ?? throw new InvalidOperationException($"Client not found for account {accountId}");

        var result = await client.Login(code);

        return result switch
        {
            "password" => new LoginResult(false, "password", "请输入两步验证密码"),
            _ when client.User != null => new LoginResult(true, null, "登录成功", MapToAccountInfo(accountId, client)),
            _ => new LoginResult(false, null, $"验证码错误或已过期: {result}")
        };
    }

    public async Task<LoginResult> SubmitPasswordAsync(int accountId, string password)
    {
        var client = _clientPool.GetClient(accountId)
            ?? throw new InvalidOperationException($"Client not found for account {accountId}");

        var result = await client.Login(password);

        return result switch
        {
            _ when client.User != null => new LoginResult(true, null, "登录成功", MapToAccountInfo(accountId, client)),
            _ => new LoginResult(false, "password", "密码错误")
        };
    }

    public Task<AccountInfo?> GetAccountInfoAsync(int accountId)
    {
        var client = _clientPool.GetClient(accountId);
        if (client?.User == null) return Task.FromResult<AccountInfo?>(null);

        return Task.FromResult<AccountInfo?>(MapToAccountInfo(accountId, client));
    }

    public async Task SyncAccountDataAsync(int accountId)
    {
        var client = _clientPool.GetClient(accountId)
            ?? throw new InvalidOperationException($"Client not found for account {accountId}");

        _logger.LogInformation("Syncing data for account {AccountId}", accountId);

        // 获取所有对话
        var dialogs = await client.Messages_GetAllDialogs();

        _logger.LogInformation("Account {AccountId} has {Count} dialogs", accountId, dialogs.Dialogs.Length);

        // TODO: 保存到数据库
    }

    public Task<AccountStatus> CheckStatusAsync(int accountId)
    {
        var client = _clientPool.GetClient(accountId);

        if (client == null)
            return Task.FromResult(AccountStatus.Offline);

        if (client.User == null)
            return Task.FromResult(AccountStatus.NeedRelogin);

        return Task.FromResult(AccountStatus.Active);
    }

    private static AccountInfo MapToAccountInfo(int accountId, WTelegram.Client client)
    {
        var user = client.User!;
        return new AccountInfo
        {
            Id = accountId,
            TelegramUserId = user.id,
            Phone = user.phone,
            Username = user.MainUsername,
            FirstName = user.first_name,
            LastName = user.last_name,
            Status = Models.AccountStatus.Active,
            LastActiveAt = DateTime.UtcNow
        };
    }
}
