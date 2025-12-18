namespace TelegramPanel.Core.Models;

/// <summary>
/// 群组信息
/// </summary>
public record GroupInfo
{
    public int Id { get; init; }
    public long TelegramId { get; init; }
    public long AccessHash { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Username { get; init; }
    public int MemberCount { get; init; }
    public int CreatorAccountId { get; init; }
    public DateTime SyncedAt { get; init; }

    public bool IsPublic => !string.IsNullOrEmpty(Username);

    public string Link => IsPublic
        ? $"https://t.me/{Username}"
        : $"https://t.me/c/{TelegramId}";
}
