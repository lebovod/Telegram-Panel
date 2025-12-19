namespace TelegramPanel.Data.Entities;

/// <summary>
/// 账号-频道关联（用于记录“某账号是某频道的创建者/管理员”）
/// </summary>
public class AccountChannel
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public int ChannelId { get; set; }

    /// <summary>
    /// 是否为创建者（拥有者）
    /// </summary>
    public bool IsCreator { get; set; }

    /// <summary>
    /// 是否为管理员（包含创建者）
    /// </summary>
    public bool IsAdmin { get; set; }

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    public Account Account { get; set; } = null!;
    public Channel Channel { get; set; } = null!;
}

