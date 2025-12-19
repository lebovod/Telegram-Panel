namespace TelegramPanel.Core.Models;

/// <summary>
/// 频道信息
/// </summary>
public record ChannelInfo
{
    public int Id { get; init; }
    public long TelegramId { get; init; }
    public long AccessHash { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Username { get; init; }
    public bool IsPublic => !string.IsNullOrEmpty(Username);
    public bool IsBroadcast { get; init; }  // true=频道, false=超级群组
    public int MemberCount { get; init; }
    public string? About { get; init; }
    /// <summary>
    /// 系统内“创建该频道”的账号（仅当本系统创建过该频道时才有值）
    /// </summary>
    public int? CreatorAccountId { get; init; }
    /// <summary>
    /// 对于当前 accountId：是否为频道创建者（拥有者）
    /// </summary>
    public bool IsCreator { get; init; }
    /// <summary>
    /// 对于当前 accountId：是否为管理员（包含创建者）
    /// </summary>
    public bool IsAdmin { get; init; }
    public int? GroupId { get; init; }
    public string? GroupName { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime SyncedAt { get; init; }

    /// <summary>
    /// 频道链接
    /// </summary>
    public string Link => IsPublic
        ? $"https://t.me/{Username}"
        : $"https://t.me/c/{TelegramId}";

    /// <summary>
    /// 频道类型显示名称
    /// </summary>
    public string TypeName => IsBroadcast ? "频道" : "超级群组";
}
