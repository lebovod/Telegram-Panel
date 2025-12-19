using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 账号-频道关联仓储实现
/// </summary>
public class AccountChannelRepository : Repository<AccountChannel>, IAccountChannelRepository
{
    public AccountChannelRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AccountChannel?> GetAsync(int accountId, int channelId)
    {
        return await _dbSet.FirstOrDefaultAsync(x => x.AccountId == accountId && x.ChannelId == channelId);
    }

    public async Task UpsertAsync(AccountChannel link)
    {
        var existing = await GetAsync(link.AccountId, link.ChannelId);
        if (existing == null)
        {
            await _dbSet.AddAsync(link);
        }
        else
        {
            existing.IsCreator = link.IsCreator;
            existing.IsAdmin = link.IsAdmin;
            existing.SyncedAt = link.SyncedAt;
            _dbSet.Update(existing);
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteForAccountExceptAsync(int accountId, IReadOnlyCollection<int> keepChannelIds)
    {
        var keep = keepChannelIds.ToHashSet();
        var toDelete = await _dbSet
            .Where(x => x.AccountId == accountId && !keep.Contains(x.ChannelId))
            .ToListAsync();

        if (toDelete.Count == 0)
            return;

        _dbSet.RemoveRange(toDelete);
        await _context.SaveChangesAsync();
    }

    public async Task<int?> GetPreferredAdminAccountIdAsync(int channelId)
    {
        return await _dbSet
            .Where(x => x.ChannelId == channelId && x.IsAdmin)
            .OrderByDescending(x => x.IsCreator)
            .ThenByDescending(x => x.SyncedAt)
            .Select(x => (int?)x.AccountId)
            .FirstOrDefaultAsync();
    }
}
