namespace TelegramPanel.Core.Models;

/// <summary>
/// 频道页脚配置（用于存储每个目标频道的独立页脚）
/// </summary>
public class ChannelFooterConfig
{
    /// <summary>
    /// 目标频道ID
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// 该频道的页脚模板（可以为空）
    /// </summary>
    public string? Footer { get; set; }
}

