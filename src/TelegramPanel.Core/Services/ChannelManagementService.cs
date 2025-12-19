using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Core.Services;

/// <summary>
/// 频道数据管理服务
/// </summary>
public class ChannelManagementService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IAccountChannelRepository _accountChannelRepository;

    public ChannelManagementService(IChannelRepository channelRepository, IAccountChannelRepository accountChannelRepository)
    {
        _channelRepository = channelRepository;
        _accountChannelRepository = accountChannelRepository;
    }

    public async Task<Channel?> GetChannelAsync(int id)
    {
        return await _channelRepository.GetByIdAsync(id);
    }

    public async Task<Channel?> GetChannelByTelegramIdAsync(long telegramId)
    {
        return await _channelRepository.GetByTelegramIdAsync(telegramId);
    }

    public async Task<IEnumerable<Channel>> GetAllChannelsAsync()
    {
        return await _channelRepository.GetAllAsync();
    }

    /// <summary>
    /// 用于列表展示：可选按账号筛选，并可选是否包含“仅管理员（非本系统创建）”频道
    /// </summary>
    public async Task<IEnumerable<Channel>> GetChannelsForViewAsync(int accountId, bool includeNonCreator)
    {
        if (accountId <= 0)
            return includeNonCreator ? await _channelRepository.GetAllAsync() : await _channelRepository.GetCreatedAsync();

        return includeNonCreator
            ? await _channelRepository.GetForAccountAsync(accountId, includeNonCreator: true)
            : await _channelRepository.GetForAccountAsync(accountId, includeNonCreator: false);
    }

    public async Task<IEnumerable<Channel>> GetChannelsByCreatorAsync(int accountId)
    {
        return await _channelRepository.GetByCreatorAccountAsync(accountId);
    }

    public async Task<IEnumerable<Channel>> GetChannelsByGroupAsync(int groupId)
    {
        return await _channelRepository.GetByGroupAsync(groupId);
    }

    public async Task<IEnumerable<Channel>> GetBroadcastChannelsAsync()
    {
        return await _channelRepository.GetBroadcastChannelsAsync();
    }

    public async Task<Channel> CreateOrUpdateChannelAsync(Channel channel)
    {
        var existing = await _channelRepository.GetByTelegramIdAsync(channel.TelegramId);
        if (existing != null)
        {
            // 更新现有频道
            existing.Title = channel.Title;
            existing.Username = channel.Username;
            existing.IsBroadcast = channel.IsBroadcast;
            existing.MemberCount = channel.MemberCount;
            existing.About = channel.About;
            existing.AccessHash = channel.AccessHash;
            existing.GroupId = channel.GroupId;
            if (existing.CreatorAccountId == null && channel.CreatorAccountId != null)
                existing.CreatorAccountId = channel.CreatorAccountId;
            if (channel.CreatedAt.HasValue)
                existing.CreatedAt = channel.CreatedAt;
            existing.SyncedAt = DateTime.UtcNow;

            await _channelRepository.UpdateAsync(existing);
            return existing;
        }
        else
        {
            // 创建新频道
            channel.SyncedAt = DateTime.UtcNow;
            return await _channelRepository.AddAsync(channel);
        }
    }

    public async Task UpdateChannelAsync(Channel channel)
    {
        channel.SyncedAt = DateTime.UtcNow;
        await _channelRepository.UpdateAsync(channel);
    }

    public async Task DeleteChannelAsync(int id)
    {
        var channel = await _channelRepository.GetByIdAsync(id);
        if (channel != null)
        {
            await _channelRepository.DeleteAsync(channel);
        }
    }

    public async Task UpdateChannelGroupAsync(int channelId, int? groupId)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId);
        if (channel != null)
        {
            channel.GroupId = groupId;
            await _channelRepository.UpdateAsync(channel);
        }
    }

    public async Task<int> GetTotalChannelCountAsync()
    {
        return await _channelRepository.CountAsync();
    }

    public async Task<int> GetChannelCountByCreatorAsync(int accountId)
    {
        return await _channelRepository.CountAsync(c => c.CreatorAccountId == accountId);
    }

    public async Task UpsertAccountChannelAsync(int accountId, int channelId, bool isCreator, bool isAdmin, DateTime syncedAtUtc)
    {
        await _accountChannelRepository.UpsertAsync(new AccountChannel
        {
            AccountId = accountId,
            ChannelId = channelId,
            IsCreator = isCreator,
            IsAdmin = isAdmin,
            SyncedAt = syncedAtUtc
        });
    }

    public async Task DeleteStaleAccountChannelsAsync(int accountId, IReadOnlyCollection<int> keepChannelIds)
    {
        await _accountChannelRepository.DeleteForAccountExceptAsync(accountId, keepChannelIds);
    }

    /// <summary>
    /// 解析频道操作的执行账号：
    /// 优先使用 preferredAccountId，其次 CreatorAccountId，否则从关联表中挑选一个管理员账号。
    /// </summary>
    public async Task<int?> ResolveExecuteAccountIdAsync(Channel channel, int? preferredAccountId = null)
    {
        if (preferredAccountId.HasValue && preferredAccountId.Value > 0)
            return preferredAccountId.Value;

        if (channel.CreatorAccountId.HasValue)
            return channel.CreatorAccountId.Value;

        return await _accountChannelRepository.GetPreferredAdminAccountIdAsync(channel.Id);
    }
}
