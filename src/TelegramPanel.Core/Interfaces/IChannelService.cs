using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 频道服务接口
/// </summary>
public interface IChannelService
{
    /// <summary>
    /// 获取账号创建的所有频道
    /// </summary>
    Task<List<ChannelInfo>> GetOwnedChannelsAsync(int accountId);

    /// <summary>
    /// 获取账号作为创建者/管理员的频道（包含“非本系统创建”的频道）
    /// </summary>
    Task<List<ChannelInfo>> GetAdminedChannelsAsync(int accountId);

    /// <summary>
    /// 创建新频道
    /// </summary>
    Task<ChannelInfo> CreateChannelAsync(int accountId, string title, string about, bool isPublic = false);

    /// <summary>
    /// 设置频道公开/私密
    /// </summary>
    Task<bool> SetChannelVisibilityAsync(int accountId, long channelId, bool isPublic, string? username = null);

    /// <summary>
    /// 邀请用户到频道
    /// </summary>
    Task<InviteResult> InviteUserAsync(int accountId, long channelId, string username);

    /// <summary>
    /// 批量邀请用户
    /// </summary>
    Task<List<InviteResult>> BatchInviteUsersAsync(int accountId, long channelId, List<string> usernames, int delayMs = 2000);

    /// <summary>
    /// 设置管理员
    /// </summary>
    Task<bool> SetAdminAsync(int accountId, long channelId, string username, AdminRights rights, string title = "Admin");

    /// <summary>
    /// 批量设置管理员
    /// </summary>
    Task<List<SetAdminResult>> BatchSetAdminsAsync(int accountId, long channelId, List<AdminRequest> requests);

    /// <summary>
    /// 设置是否允许转发（关闭后为“保护内容”，禁止转发/保存）
    /// </summary>
    Task<bool> SetForwardingAllowedAsync(int accountId, long channelId, bool allowed);

    /// <summary>
    /// 获取频道管理员列表
    /// </summary>
    Task<List<ChannelAdminInfo>> GetAdminsAsync(int accountId, long channelId);

    /// <summary>
    /// 编辑频道信息（标题/简介）
    /// </summary>
    Task<bool> UpdateChannelInfoAsync(int accountId, long channelId, string title, string? about);

    /// <summary>
    /// 从频道踢出用户（通过 username），可选是否永久封禁
    /// </summary>
    Task<bool> KickUserAsync(int accountId, long channelId, string username, bool permanentBan = false);
}

/// <summary>
/// 邀请结果
/// </summary>
public record InviteResult(string Username, bool Success, string? Error = null);

/// <summary>
/// 设置管理员结果
/// </summary>
public record SetAdminResult(string Username, bool Success, string? Error = null);

/// <summary>
/// 管理员请求
/// </summary>
public record AdminRequest(string Username, AdminRights Rights, string Title = "Admin");

/// <summary>
/// 管理员权限
/// </summary>
[Flags]
public enum AdminRights
{
    None = 0,
    ChangeInfo = 1,
    PostMessages = 2,
    EditMessages = 4,
    DeleteMessages = 8,
    BanUsers = 16,
    InviteUsers = 32,
    PinMessages = 64,
    ManageCall = 128,
    AddAdmins = 256,
    Anonymous = 512,
    ManageTopics = 1024,

    // 常用组合
    BasicAdmin = ChangeInfo | PostMessages | EditMessages | DeleteMessages | BanUsers | InviteUsers | PinMessages,
    FullAdmin = BasicAdmin | ManageCall | AddAdmins
}
