using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 账号服务接口
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// 发起手机号登录（发送验证码）
    /// </summary>
    Task<LoginResult> StartLoginAsync(int accountId, string phone);

    /// <summary>
    /// 提交验证码完成登录
    /// </summary>
    Task<LoginResult> SubmitCodeAsync(int accountId, string code);

    /// <summary>
    /// 提交两步验证密码
    /// </summary>
    Task<LoginResult> SubmitPasswordAsync(int accountId, string password);

    /// <summary>
    /// 获取账号信息
    /// </summary>
    Task<AccountInfo?> GetAccountInfoAsync(int accountId);

    /// <summary>
    /// 同步账号数据（频道、群组）
    /// </summary>
    Task SyncAccountDataAsync(int accountId);

    /// <summary>
    /// 检查账号状态
    /// </summary>
    Task<AccountStatus> CheckStatusAsync(int accountId);
}

/// <summary>
/// 登录结果
/// </summary>
public record LoginResult(
    bool Success,
    string? NextStep,  // null=完成, "code"=需要验证码, "password"=需要密码, "signup"=需要注册
    string? Message,
    AccountInfo? Account = null
);

/// <summary>
/// 账号状态
/// </summary>
public enum AccountStatus
{
    Active,
    Offline,
    Banned,
    Limited,
    NeedRelogin
}
