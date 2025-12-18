using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TL;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// 群组服务实现
/// </summary>
public class GroupService : IGroupService
{
    private readonly ITelegramClientPool _clientPool;
    private readonly ILogger<GroupService> _logger;

    public GroupService(ITelegramClientPool clientPool, ILogger<GroupService> logger)
    {
        _clientPool = clientPool;
        _logger = logger;
    }

    public async Task<List<GroupInfo>> GetOwnedGroupsAsync(int accountId)
    {
        var client = _clientPool.GetClient(accountId)
            ?? throw new InvalidOperationException($"Client not found for account {accountId}");

        var ownedGroups = new List<GroupInfo>();
        var dialogs = await client.Messages_GetAllDialogs();

        foreach (var (id, chat) in dialogs.chats)
        {
            // 处理基础群组（Chat类型，非Channel）
            // 注意：基础 Chat 类型无法直接判断创建者，需要通过 GetFullChat 获取
            if (chat is Chat basicChat && basicChat.IsActive)
            {
                try
                {
                    // 获取完整信息来判断是否为创建者
                    var fullChat = await client.Messages_GetFullChat(basicChat.id);
                    if (fullChat.full_chat is ChatFull cf && cf.participants is ChatParticipants cp)
                    {
                        // 检查当前用户是否为创建者
                        var creator = cp.participants.OfType<ChatParticipantCreator>()
                            .FirstOrDefault(p => p.user_id == client.User!.id);
                        if (creator != null)
                        {
                            ownedGroups.Add(new GroupInfo
                            {
                                TelegramId = basicChat.id,
                                Title = basicChat.title,
                                MemberCount = basicChat.participants_count,
                                CreatorAccountId = accountId,
                                SyncedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get full chat info for {ChatId}", basicChat.id);
                }
            }
            // 处理超级群组（Channel类型但megagroup=true, 即 !IsChannel）
            else if (chat is Channel channel && channel.IsActive && !channel.IsChannel)
            {
                try
                {
                    // 通过获取管理员列表来检查当前用户是否为创建者
                    var participants = await client.Channels_GetParticipants(channel, new ChannelParticipantsAdmins());
                    var isCreator = participants.participants
                        .OfType<ChannelParticipantCreator>()
                        .Any(p => p.user_id == client.User!.id);

                    if (!isCreator) continue;

                    var fullChannel = await client.Channels_GetFullChannel(channel);
                    ownedGroups.Add(new GroupInfo
                    {
                        TelegramId = channel.id,
                        AccessHash = channel.access_hash,
                        Title = channel.title,
                        Username = channel.MainUsername,
                        MemberCount = fullChannel.full_chat.ParticipantsCount,
                        CreatorAccountId = accountId,
                        SyncedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get full group info for {GroupId}", channel.id);
                }
            }
        }

        _logger.LogInformation("Found {Count} owned groups for account {AccountId}", ownedGroups.Count, accountId);
        return ownedGroups;
    }

    public async Task<GroupInfo?> GetGroupInfoAsync(int accountId, long groupId)
    {
        var groups = await GetOwnedGroupsAsync(accountId);
        return groups.FirstOrDefault(g => g.TelegramId == groupId);
    }
}
