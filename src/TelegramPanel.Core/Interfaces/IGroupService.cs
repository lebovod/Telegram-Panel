using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 群组服务接口
/// </summary>
public interface IGroupService
{
    /// <summary>
    /// 获取账号创建的所有群组
    /// </summary>
    Task<List<GroupInfo>> GetOwnedGroupsAsync(int accountId);

    /// <summary>
    /// 获取群组详情
    /// </summary>
    Task<GroupInfo?> GetGroupInfoAsync(int accountId, long groupId);
}
