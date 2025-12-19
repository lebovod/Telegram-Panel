using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 账号-频道关联仓储接口
/// </summary>
public interface IAccountChannelRepository : IRepository<AccountChannel>
{
    Task<AccountChannel?> GetAsync(int accountId, int channelId);
    Task UpsertAsync(AccountChannel link);
    Task DeleteForAccountExceptAsync(int accountId, IReadOnlyCollection<int> keepChannelIds);
    Task<int?> GetPreferredAdminAccountIdAsync(int channelId);
}
