namespace TelegramPanel.Core.Models;

/// <summary>
/// 账号信息
/// </summary>
public record AccountInfo
{
    public int Id { get; init; }
    public long TelegramUserId { get; init; }
    public string? Phone { get; init; }
    public string? Username { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhotoPath { get; init; }
    public AccountStatus Status { get; init; }
    public DateTime? LastActiveAt { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Username)
        ? $"{FirstName} {LastName}".Trim()
        : $"@{Username}";
}

/// <summary>
/// 账号状态枚举
/// </summary>
public enum AccountStatus
{
    Active,      // 正常
    Offline,     // 离线
    Banned,      // 被封禁
    Limited,     // 受限
    NeedRelogin  // 需要重新登录
}
