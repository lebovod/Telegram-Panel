namespace TelegramPanel.Data.Entities;

/// <summary>
/// 频道转发规则
/// </summary>
public class ChannelForwardRule
{
    /// <summary>
    /// 规则ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 规则名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 机器人ID
    /// </summary>
    public int BotId { get; set; }

    /// <summary>
    /// 源频道ID（Telegram 频道ID）
    /// </summary>
    public long SourceChannelId { get; set; }

    /// <summary>
    /// 源频道用户名
    /// </summary>
    public string? SourceChannelUsername { get; set; }

    /// <summary>
    /// 源频道标题
    /// </summary>
    public string? SourceChannelTitle { get; set; }

    /// <summary>
    /// 目标频道ID列表（逗号分隔）- 保留用于兼容旧数据
    /// </summary>
    public string TargetChannelIds { get; set; } = string.Empty;

    /// <summary>
    /// 目标频道配置（JSON 数组，包含每个频道的独立页脚）
    /// 示例: [{"ChannelId": -100123, "Footer": "页脚A"}, {"ChannelId": -100456, "Footer": "页脚B"}]
    /// </summary>
    public string? TargetChannelsConfig { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 帖子底部附加内容（支持 Markdown）- 保留用于兼容旧数据或作为默认页脚
    /// </summary>
    public string? FooterTemplate { get; set; }

    /// <summary>
    /// 删除关键词配置（JSON 数组）
    /// 示例: ["✅ CẬP NHẬT TIN", "#付费广告"]
    /// </summary>
    public string? DeleteAfterKeywords { get; set; }

    /// <summary>
    /// 删除模式配置（JSON 数组）
    /// 示例: ["@\\w+", "https?://\\S+", "t.me/\\S+"]
    /// </summary>
    public string? DeletePatterns { get; set; }

    /// <summary>
    /// 是否删除链接
    /// </summary>
    public bool DeleteLinks { get; set; } = false;

    /// <summary>
    /// 是否删除 @ 提及
    /// </summary>
    public bool DeleteMentions { get; set; } = false;

    /// <summary>
    /// 最后处理时间
    /// </summary>
    public DateTime? LastProcessedAt { get; set; }

    /// <summary>
    /// 最后处理的消息ID
    /// </summary>
    public int? LastProcessedMessageId { get; set; }

    /// <summary>
    /// 已转发消息数量
    /// </summary>
    public int ForwardedCount { get; set; }

    /// <summary>
    /// 跳过消息数量
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 关联的机器人
    /// </summary>
    public Bot? Bot { get; set; }
}

