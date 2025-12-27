namespace TelegramPanel.Core.Models;

/// <summary>
/// Telegram 全局搜索结果（用户、频道、群组等）
/// </summary>
public record TelegramSearchResult(
    long Id,
    long? AccessHash,
    string Type,           // "User", "Channel", "Chat"
    string? Title,         // 频道/群组名称
    string? Username,      // 用户名（不含@）
    string? FirstName,     // 用户姓
    string? LastName,      // 用户名
    string? About,         // 简介
    int? ParticipantsCount, // 成员数
    bool IsChannel,        // 是否是频道
    bool IsGroup,          // 是否是群组
    bool IsUser,           // 是否是用户
    bool IsVerified,       // 是否已验证
    bool IsScam,           // 是否标记为诈骗
    bool IsFake,           // 是否标记为假冒
    string? PhotoUrl       // 头像URL（如果有）
)
{
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;
            
            var fullName = $"{FirstName} {LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName;
            
            if (!string.IsNullOrWhiteSpace(Username))
                return $"@{Username}";
            
            return Id.ToString();
        }
    }

    /// <summary>
    /// 显示类型
    /// </summary>
    public string DisplayType
    {
        get
        {
            if (IsChannel) return "频道";
            if (IsGroup) return "群组";
            if (IsUser) return "用户";
            return Type;
        }
    }

    /// <summary>
    /// 用于订阅的标识（优先使用用户名，否则使用ID）
    /// </summary>
    public string SubscribeIdentifier
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Username))
                return $"@{Username}";
            return Id.ToString();
        }
    }
}

