using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 频道转发监听服务
/// 监听频道消息并根据配置的规则自动转发到目标频道，支持媒体组（相册）
/// </summary>
public class ChannelForwardMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChannelForwardMonitorService> _logger;
    private readonly BotUpdateHub _updateHub;
    private readonly ConcurrentDictionary<int, BotUpdateHub.BotUpdateSubscription> _activeSubscriptions = new();
    
    // 跟踪已处理的媒体组ID，避免重复处理同一相册
    private readonly ConcurrentDictionary<string, DateTime> _processedMediaGroups = new();
    // 正在处理的媒体组ID
    private readonly ConcurrentDictionary<string, bool> _processingMediaGroups = new();
    // 缓存媒体组消息，等待收集完整
    private readonly ConcurrentDictionary<string, List<JsonElement>> _mediaGroupCache = new();

    public ChannelForwardMonitorService(
        IServiceScopeFactory scopeFactory,
        BotUpdateHub updateHub,
        ILogger<ChannelForwardMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _updateHub = updateHub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("频道转发监听服务已启动");

        // 等待应用完全启动
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        _logger.LogInformation("频道转发功能已就绪。您可以在 '机器人管理 -> 频道转发' 中配置转发规则。");

        // 定期刷新订阅和处理消息
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshSubscriptionsAsync(stoppingToken);
                await ProcessPendingMessagesAsync(stoppingToken);
                CleanupOldMediaGroups();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "频道转发监听循环出错");
            }

            // 每2秒处理一次（减少延迟）
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        // 清理订阅
        foreach (var sub in _activeSubscriptions.Values)
        {
            try { await sub.DisposeAsync(); } catch { }
        }
        _activeSubscriptions.Clear();

        _logger.LogInformation("频道转发监听服务已停止");
    }

    /// <summary>
    /// 清理超过5分钟的旧媒体组记录
    /// </summary>
    private void CleanupOldMediaGroups()
    {
        var now = DateTime.UtcNow;
        var oldGroups = _processedMediaGroups
            .Where(kv => (now - kv.Value).TotalMinutes > 5)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var groupId in oldGroups)
        {
            _processedMediaGroups.TryRemove(groupId, out _);
            _mediaGroupCache.TryRemove(groupId, out _);
        }
    }

    /// <summary>
    /// 刷新Bot订阅：确保所有启用规则的Bot都有活跃订阅
    /// </summary>
    private async Task RefreshSubscriptionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ruleService = scope.ServiceProvider.GetRequiredService<ChannelForwardManagementService>();

        var enabledRules = await ruleService.GetEnabledRulesAsync();
        var activeBotIds = enabledRules.Select(r => r.BotId).Distinct().ToHashSet();

        // 订阅新的Bot
        foreach (var botId in activeBotIds)
        {
            if (_activeSubscriptions.ContainsKey(botId))
                continue;

            try
            {
                var subscription = await _updateHub.SubscribeAsync(botId, cancellationToken);
                _activeSubscriptions[botId] = subscription;
                _logger.LogInformation("已订阅机器人 {BotId} 的频道消息更新", botId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "订阅机器人 {BotId} 失败", botId);
            }
        }

        // 取消不需要的订阅
        foreach (var botId in _activeSubscriptions.Keys.ToList())
        {
            if (!activeBotIds.Contains(botId))
            {
                if (_activeSubscriptions.TryRemove(botId, out var sub))
                {
                    try { await sub.DisposeAsync(); } catch { }
                    _logger.LogInformation("已取消订阅机器人 {BotId}", botId);
                }
            }
        }
    }

    /// <summary>
    /// 处理所有订阅的待处理消息
    /// </summary>
    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ruleService = scope.ServiceProvider.GetRequiredService<ChannelForwardManagementService>();
        var botApi = scope.ServiceProvider.GetRequiredService<TelegramBotApiClient>();
        var botManagement = scope.ServiceProvider.GetRequiredService<BotManagementService>();

        var enabledRules = await ruleService.GetEnabledRulesAsync();
        var rulesByBotId = enabledRules.GroupBy(r => r.BotId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (botId, subscription) in _activeSubscriptions)
        {
            // 获取该Bot的所有规则
            if (!rulesByBotId.TryGetValue(botId, out var rules) || rules.Count == 0)
                continue;

            var bot = await botManagement.GetBotAsync(botId);
            if (bot == null || !bot.IsActive)
                continue;

            // 处理所有待处理的消息
            while (subscription.Reader.TryRead(out var update))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessUpdateAsync(update, rules, bot.Token, botApi, ruleService, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "处理消息更新失败 (BotId: {BotId})", botId);
                }
            }
        }

        // 处理缓存的媒体组（如果超过3秒没有新消息，就发送）
        await ProcessCachedMediaGroupsAsync(botApi, ruleService, cancellationToken);
    }

    /// <summary>
    /// 处理缓存的媒体组
    /// </summary>
    private async Task ProcessCachedMediaGroupsAsync(
        TelegramBotApiClient botApi,
        ChannelForwardManagementService ruleService,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        using var scope = _scopeFactory.CreateScope();
        var botManagement = scope.ServiceProvider.GetRequiredService<BotManagementService>();
        var enabledRules = await ruleService.GetEnabledRulesAsync();
        
        foreach (var (mediaGroupId, messages) in _mediaGroupCache.ToList())
        {
            if (messages.Count == 0)
                continue;

            // 如果最后一条消息超过3秒，认为媒体组已完整
            var firstMessage = messages.First();
            if (!firstMessage.TryGetProperty("channel_post", out var post))
                continue;

            if (!post.TryGetProperty("date", out var dateEl) || !dateEl.TryGetInt64(out var timestamp))
                continue;

            var messageTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            if ((now - messageTime).TotalSeconds < 3)
                continue; // 还太新，继续等待

            // 标记为正在处理
            if (!_processingMediaGroups.TryAdd(mediaGroupId, true))
                continue;

            try
            {
                _logger.LogInformation("处理媒体组 {MediaGroupId}，包含 {Count} 条消息", mediaGroupId, messages.Count);
                
                // 获取第一条消息的详细信息
                var sourceChatId = post.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chatIdEl) && chatIdEl.TryGetInt64(out var cid) ? cid : 0;
                
                if (sourceChatId == 0)
                {
                    _logger.LogDebug("媒体组源频道ID无效，跳过并清理 - 媒体组ID: {MediaGroupId}", mediaGroupId);
                    _mediaGroupCache.TryRemove(mediaGroupId, out _);
                    _processedMediaGroups[mediaGroupId] = now;
                    continue;
                }

                // 查找匹配的规则
                var matchedRules = enabledRules.Where(r => r.SourceChannelId == sourceChatId).ToList();
                if (matchedRules.Count == 0)
                {
                    _logger.LogDebug("媒体组没有匹配规则，跳过并清理 - 源频道ID: {SourceId}, 媒体组ID: {MediaGroupId}", 
                        sourceChatId, mediaGroupId);
                    _mediaGroupCache.TryRemove(mediaGroupId, out _);
                    _processedMediaGroups[mediaGroupId] = now;
                    continue;
                }

                // 提取所有消息ID
                var messageIds = messages
                    .Select(m => m.TryGetProperty("channel_post", out var cp) && cp.TryGetProperty("message_id", out var mid) && mid.TryGetInt32(out var id) ? id : 0)
                    .Where(id => id > 0)
                    .OrderBy(id => id)
                    .ToList();

                if (messageIds.Count == 0)
                {
                    _logger.LogWarning("媒体组没有有效消息ID，跳过并清理 - 媒体组ID: {MediaGroupId}", mediaGroupId);
                    _mediaGroupCache.TryRemove(mediaGroupId, out _);
                    _processedMediaGroups[mediaGroupId] = now;
                    continue;
                }

                _logger.LogInformation("媒体组包含消息ID: {MessageIds}", string.Join(", ", messageIds));

                // 对每个匹配的规则进行转发
                foreach (var rule in matchedRules)
                {
                    // 检查是否需要跳过（检查第一条消息的文本）
                    if (ShouldSkipMessage(post, rule))
                    {
                        _logger.LogDebug("媒体组被跳过 - 规则: {RuleName}, 媒体组ID: {MediaGroupId}", rule.Name, mediaGroupId);
                        rule.SkippedCount++;
                        rule.UpdatedAt = DateTime.UtcNow;
                        await ruleService.UpdateRuleAsync(rule);
                        continue;
                    }

                    var bot = await botManagement.GetBotAsync(rule.BotId);
                    if (bot == null || !bot.IsActive)
                        continue;

                    // 解析目标频道列表
                    var targetChannelIds = rule.TargetChannelIds
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => long.TryParse(s.Trim(), out var id) ? id : (long?)null)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToList();

                    if (targetChannelIds.Count == 0)
                        continue;

                    // 获取文本内容（从第一条消息）
                    var messageText = GetMessageText(post);

                    // 转发到每个目标频道（每个频道使用独立的页脚）
                    foreach (var targetChatId in targetChannelIds)
                    {
                        try
                        {
                            // 获取该频道的页脚配置
                            var channelFooter = GetFooterForChannel(rule, targetChatId);
                            
                            // 为该频道处理文本
                            var processedText = ProcessMessageText(messageText, rule, channelFooter);
                            
                            // 如果返回null，表示应该跳过该消息
                            if (processedText == null && !string.IsNullOrEmpty(messageText))
                            {
                                _logger.LogInformation("消息被过滤规则拦截，跳过转发到频道 {ChannelId} - 规则: {RuleName}", targetChatId, rule.Name);
                                rule.SkippedCount++;
                                continue; // 跳过这个频道，继续下一个
                            }
                            
                            var needsModification = NeedsModification(rule, channelFooter);

                            // 构建媒体组数据
                            var mediaItems = new List<Dictionary<string, object>>();
                            
                            for (int i = 0; i < messages.Count; i++)
                            {
                                var msg = messages[i];
                                if (!msg.TryGetProperty("channel_post", out var msgPost))
                                    continue;

                                // 提取媒体信息
                                var mediaItem = ExtractMediaFromMessage(msgPost);
                                if (mediaItem == null)
                                    continue;

                                // 只有第一张图片带caption
                                if (i == 0 && needsModification)
                                {
                                    // 如果需要修改，使用处理后的文本（即使是空字符串）
                                    mediaItem["caption"] = processedText ?? "";
                                }

                                mediaItems.Add(mediaItem);
                            }

                            if (mediaItems.Count == 0)
                            {
                                _logger.LogWarning("媒体组中没有有效的媒体项");
                                continue;
                            }

                            // 使用 sendMediaGroup 发送媒体组
                            await SendMediaGroupAsync(
                                bot.Token,
                                botApi,
                                targetChatId,
                                mediaItems,
                                cancellationToken);

                            rule.ForwardedCount += mediaItems.Count;
                            _logger.LogInformation("媒体组已转发 - 规则: {RuleName}, 源: {Source}, 目标: {Target}, 图片数: {Count}",
                                rule.Name, sourceChatId, targetChatId, mediaItems.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "转发媒体组到目标频道失败 - 规则: {RuleName}, 目标: {Target}",
                                rule.Name, targetChatId);
                            rule.SkippedCount++;
                        }
                    }

                    // 更新规则统计
                    rule.LastProcessedAt = DateTime.UtcNow;
                    rule.LastProcessedMessageId = messageIds.Last();
                    rule.UpdatedAt = DateTime.UtcNow;
                    await ruleService.UpdateRuleAsync(rule);
                }

                _mediaGroupCache.TryRemove(mediaGroupId, out _);
                _processedMediaGroups[mediaGroupId] = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理媒体组失败 - 媒体组ID: {MediaGroupId}", mediaGroupId);
            }
            finally
            {
                _processingMediaGroups.TryRemove(mediaGroupId, out _);
            }
        }
    }

    /// <summary>
    /// 处理单个更新消息
    /// </summary>
    private async Task ProcessUpdateAsync(
        JsonElement update,
        List<ChannelForwardRule> rules,
        string botToken,
        TelegramBotApiClient botApi,
        ChannelForwardManagementService ruleService,
        CancellationToken cancellationToken)
    {
        if (update.ValueKind != JsonValueKind.Object)
            return;

        // 只处理频道消息（channel_post）
        if (!update.TryGetProperty("channel_post", out var channelPost) || channelPost.ValueKind != JsonValueKind.Object)
            return;

        var message = channelPost;

        // 获取源频道ID
        if (!message.TryGetProperty("chat", out var chat) || 
            !chat.TryGetProperty("id", out var chatIdEl) ||
            !chatIdEl.TryGetInt64(out var sourceChatId))
            return;

        // 获取消息ID
        if (!message.TryGetProperty("message_id", out var msgIdEl) ||
            !msgIdEl.TryGetInt32(out var messageId))
            return;

        // 检查是否是媒体组
        string? mediaGroupId = null;
        if (message.TryGetProperty("media_group_id", out var mediaGroupIdEl))
        {
            mediaGroupId = mediaGroupIdEl.GetString();
        }

        // 如果是媒体组，先缓存起来
        if (!string.IsNullOrEmpty(mediaGroupId))
        {
            // 检查是否已经处理过
            if (_processedMediaGroups.ContainsKey(mediaGroupId))
            {
                _logger.LogDebug("媒体组 {MediaGroupId} 已处理，跳过", mediaGroupId);
                return;
            }

            // 添加到缓存
            _mediaGroupCache.AddOrUpdate(
                mediaGroupId,
                _ => new List<JsonElement> { update.Clone() },
                (_, list) =>
                {
                    list.Add(update.Clone());
                    return list;
                });

            _logger.LogDebug("缓存媒体组消息 - 组ID: {MediaGroupId}, 当前数量: {Count}", 
                mediaGroupId, _mediaGroupCache[mediaGroupId].Count);

            // 等待更多消息，稍后统一处理
            return;
        }

        // 非媒体组消息，直接处理
        var matchedRules = rules.Where(r => r.SourceChannelId == sourceChatId).ToList();
        if (matchedRules.Count == 0)
            return;

        _logger.LogInformation("收到频道消息 - 源频道ID: {SourceId}, 消息ID: {MessageId}, 匹配规则数: {RuleCount}",
            sourceChatId, messageId, matchedRules.Count);

        // 对每个匹配的规则进行转发
        foreach (var rule in matchedRules)
        {
            try
            {
                await ForwardSingleMessageAsync(rule, botToken, botApi, sourceChatId, messageId, message, ruleService, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "转发消息失败 - 规则: {RuleName}, 源频道: {SourceId}, 消息ID: {MessageId}",
                    rule.Name, sourceChatId, messageId);

                rule.SkippedCount++;
                rule.UpdatedAt = DateTime.UtcNow;
                await ruleService.UpdateRuleAsync(rule);
            }
        }
    }

    /// <summary>
    /// 转发单条消息
    /// </summary>
    private async Task ForwardSingleMessageAsync(
        ChannelForwardRule rule,
        string botToken,
        TelegramBotApiClient botApi,
        long sourceChatId,
        int messageId,
        JsonElement message,
        ChannelForwardManagementService ruleService,
        CancellationToken cancellationToken)
    {
        // 检查消息是否应该被跳过
        if (ShouldSkipMessage(message, rule))
        {
            _logger.LogDebug("消息被跳过 - 规则: {RuleName}, 源频道: {SourceId}, 消息ID: {MessageId}",
                rule.Name, sourceChatId, messageId);
            
            rule.SkippedCount++;
            rule.UpdatedAt = DateTime.UtcNow;
            await ruleService.UpdateRuleAsync(rule);
            return;
        }

        // 解析目标频道列表
        var targetChannelIds = rule.TargetChannelIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (targetChannelIds.Count == 0)
        {
            _logger.LogWarning("规则 {RuleName} 没有有效的目标频道", rule.Name);
            return;
        }

        // 获取消息内容
        var messageText = GetMessageText(message);

        // 检查消息是否包含媒体
        var hasMedia = message.TryGetProperty("photo", out _) || 
                       message.TryGetProperty("video", out _) || 
                       message.TryGetProperty("document", out _) ||
                       message.TryGetProperty("audio", out _) ||
                       message.TryGetProperty("voice", out _) ||
                       message.TryGetProperty("animation", out _);

        // 转发到每个目标频道（每个频道使用独立的页脚）
        foreach (var targetChatId in targetChannelIds)
        {
            try
            {
                // 获取该频道的页脚配置
                var channelFooter = GetFooterForChannel(rule, targetChatId);
                
                // 为该频道处理文本
                var processedText = ProcessMessageText(messageText, rule, channelFooter);

                // 如果返回null，表示应该跳过该消息
                if (processedText == null && !string.IsNullOrEmpty(messageText))
                {
                    _logger.LogInformation("消息被过滤规则拦截，跳过转发到频道 {ChannelId} - 规则: {RuleName}, 消息ID: {MsgId}", 
                        targetChatId, rule.Name, messageId);
                    rule.SkippedCount++;
                    continue; // 跳过这个频道，继续下一个
                }

                var needsModification = NeedsModification(rule, channelFooter);

                // 如果是纯文本消息且需要修改，使用sendMessage发送处理后的文本
                if (!hasMedia && needsModification && !string.IsNullOrEmpty(processedText))
                {
                    _logger.LogInformation("纯文本消息需要修改，使用sendMessage发送 - 规则: {RuleName}, 目标: {Target}", rule.Name, targetChatId);
                    
                    var sendParams = new Dictionary<string, string?>
                    {
                        ["chat_id"] = targetChatId.ToString(),
                        ["text"] = processedText
                    };
                    
                    await botApi.CallAsync(botToken, "sendMessage", sendParams, cancellationToken);
                }
                else
                {
                    // 媒体消息或不需要修改的纯文本消息，使用copyMessage
                    await CopyMessageAsync(
                        botToken, 
                        botApi, 
                        sourceChatId, 
                        messageId, 
                        targetChatId, 
                        processedText,
                        needsModification,
                        cancellationToken);
                }

                rule.ForwardedCount++;
                _logger.LogInformation("消息已转发 - 规则: {RuleName}, 源: {Source}, 目标: {Target}, 消息ID: {MsgId}",
                    rule.Name, sourceChatId, targetChatId, messageId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "转发到目标频道失败 - 规则: {RuleName}, 目标: {Target}",
                    rule.Name, targetChatId);
                rule.SkippedCount++;
            }
        }

        // 更新规则统计
        rule.LastProcessedAt = DateTime.UtcNow;
        rule.LastProcessedMessageId = messageId;
        rule.UpdatedAt = DateTime.UtcNow;
        await ruleService.UpdateRuleAsync(rule);
    }

    /// <summary>
    /// 判断消息是否应该被跳过（只检查正则表达式过滤）
    /// 注意：DeleteAfterKeywords 用于删除关键词后的内容，不用于跳过消息
    /// </summary>
    private bool ShouldSkipMessage(JsonElement message, ChannelForwardRule rule)
    {
        var text = GetMessageText(message);
        if (string.IsNullOrEmpty(text))
            return false;

        // 只检查正则表达式过滤
        if (!string.IsNullOrEmpty(rule.DeletePatterns))
        {
            var patterns = rule.DeletePatterns.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p));

            foreach (var pattern in patterns)
            {
                try
                {
                    if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                    {
                        _logger.LogDebug("消息匹配正则表达式 '{Pattern}'，将被跳过", pattern);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "正则表达式无效: {Pattern}", pattern);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 获取消息文本
    /// </summary>
    private string? GetMessageText(JsonElement message)
    {
        if (message.TryGetProperty("text", out var textEl))
            return textEl.GetString();

        if (message.TryGetProperty("caption", out var captionEl))
            return captionEl.GetString();

        return null;
    }

    /// <summary>
    /// 处理消息文本（删除关键词后内容，检查链接和提及）
    /// 返回 null 表示应该跳过该消息
    /// </summary>
    private string? ProcessMessageText(string? text, ChannelForwardRule rule, string? customFooter = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogDebug("消息文本为空，跳过处理");
            return text;
        }

        var originalText = text;
        var processed = text;

        _logger.LogInformation("开始处理消息文本，原文长度: {Length}", text.Length);

        // 步骤1：先删除关键词及其后面的所有内容
        if (!string.IsNullOrEmpty(rule.DeleteAfterKeywords))
        {
            List<string> keywords;
            try
            {
                // 尝试从JSON反序列化
                keywords = JsonSerializer.Deserialize<List<string>>(rule.DeleteAfterKeywords) ?? new List<string>();
                _logger.LogInformation("从JSON反序列化得到 {Count} 个关键词", keywords.Count);
            }
            catch
            {
                // 如果不是JSON，则按逗号分割（向后兼容）
                keywords = rule.DeleteAfterKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList();
                _logger.LogInformation("按逗号分割得到 {Count} 个关键词", keywords.Count);
            }

            _logger.LogInformation("配置的删除关键词: [{Keywords}]", string.Join(", ", keywords));

            foreach (var keyword in keywords)
            {
                _logger.LogInformation("正在搜索关键词: '{Keyword}'", keyword);
                var index = processed.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // 找到关键词，删除从该位置开始的所有内容
                    var before = processed.Substring(0, index);
                    _logger.LogInformation("✅ 找到关键词 '{Keyword}' 在位置 {Index}，删除后文本长度: {Before} -> {After}", 
                        keyword, index, processed.Length, before.Length);
                    processed = before;
                    break; // 只处理第一个匹配的关键词
                }
                else
                {
                    _logger.LogInformation("❌ 未找到关键词 '{Keyword}'", keyword);
                }
            }
        }

        // 步骤2：检查删除关键词后的文本是否包含链接（如果DeleteLinks=true，则跳过包含链接的消息）
        if (rule.DeleteLinks)
        {
            // 检查是否包含 http/https 链接
            if (Regex.IsMatch(processed, @"https?://[^\s]+", RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("❌ 检测到http/https链接，跳过整个消息");
                return null; // 返回null表示跳过该消息
            }
            // 检查是否包含 t.me 链接
            if (Regex.IsMatch(processed, @"t\.me/[^\s]+", RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("❌ 检测到t.me链接，跳过整个消息");
                return null; // 返回null表示跳过该消息
            }
            _logger.LogInformation("✅ 未检测到链接，继续处理");
        }

        // 步骤3：检查是否包含用户名提及（如果DeleteMentions=true，则跳过包含@提及的消息）
        if (rule.DeleteMentions)
        {
            if (Regex.IsMatch(processed, @"@\w+"))
            {
                _logger.LogInformation("❌ 检测到@用户名提及，跳过整个消息");
                return null; // 返回null表示跳过该消息
            }
            _logger.LogInformation("✅ 未检测到@提及，继续处理");
        }

        // 步骤4：添加页脚（优先使用customFooter，如果没有则使用rule.FooterTemplate作为默认）
        var footerToUse = customFooter ?? rule.FooterTemplate;
        if (!string.IsNullOrEmpty(footerToUse))
        {
            processed = processed.TrimEnd() + "\n\n" + footerToUse;
            _logger.LogInformation("添加页脚，最终文本长度: {Length}", processed.Length);
        }

        var result = processed?.Trim();
        
        _logger.LogInformation("✅ 文本处理完成，原文长度: {Original}, 处理后长度: {Processed}", 
            originalText.Length, result?.Length ?? 0);

        return result;
    }

    /// <summary>
    /// 判断消息是否需要修改
    /// </summary>
    private bool NeedsModification(ChannelForwardRule rule, string? customFooter = null)
    {
        return rule.DeleteLinks 
            || rule.DeleteMentions 
            || !string.IsNullOrEmpty(customFooter)
            || !string.IsNullOrEmpty(rule.FooterTemplate)
            || !string.IsNullOrEmpty(rule.DeleteAfterKeywords);
    }

    /// <summary>
    /// 从规则的TargetChannelsConfig中获取特定频道的页脚
    /// </summary>
    private string? GetFooterForChannel(ChannelForwardRule rule, long channelId)
    {
        if (string.IsNullOrEmpty(rule.TargetChannelsConfig))
        {
            // 如果没有TargetChannelsConfig，使用默认的FooterTemplate
            return rule.FooterTemplate;
        }

        try
        {
            var configs = JsonSerializer.Deserialize<List<TelegramPanel.Core.Models.ChannelFooterConfig>>(rule.TargetChannelsConfig);
            var config = configs?.FirstOrDefault(c => c.ChannelId == channelId);
            return config?.Footer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析TargetChannelsConfig失败，使用默认页脚");
            return rule.FooterTemplate;
        }
    }

    /// <summary>
    /// 复制消息到目标频道（使用copyMessage，不会显示"转发自"标记）
    /// </summary>
    private async Task CopyMessageAsync(
        string botToken,
        TelegramBotApiClient botApi,
        long fromChatId,
        int messageId,
        long toChatId,
        string? processedText,
        bool modifyCaption,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["chat_id"] = toChatId.ToString(),
            ["from_chat_id"] = fromChatId.ToString(),
            ["message_id"] = messageId.ToString()
        };

        // 如果需要修改文本/caption，则添加caption参数
        if (modifyCaption && !string.IsNullOrEmpty(processedText))
        {
            parameters["caption"] = processedText;
        }

        try
        {
            // 使用 copyMessage API：复制消息但不显示"转发自"标记
            await botApi.CallAsync(botToken, "copyMessage", parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "copyMessage失败（可能是特殊消息类型如礼物/投票等无法复制），跳过该消息 - 源: {From}, 目标: {To}, 消息ID: {MsgId}",
                fromChatId, toChatId, messageId);
            
            // 不使用 forwardMessage 回退，直接抛出异常让调用方处理（增加 SkippedCount）
            throw;
        }
    }

    /// <summary>
    /// 从消息中提取媒体信息（用于 sendMediaGroup）
    /// </summary>
    private Dictionary<string, object>? ExtractMediaFromMessage(JsonElement message)
    {
        try
        {
            // 检查照片
            if (message.TryGetProperty("photo", out var photoArray) && photoArray.ValueKind == JsonValueKind.Array)
            {
                // 获取最大尺寸的照片
                var largestPhoto = photoArray.EnumerateArray()
                    .OrderByDescending(p => p.TryGetProperty("file_size", out var fs) && fs.TryGetInt32(out var size) ? size : 0)
                    .FirstOrDefault();

                if (largestPhoto.ValueKind != JsonValueKind.Undefined && 
                    largestPhoto.TryGetProperty("file_id", out var fileIdEl))
                {
                    var fileId = fileIdEl.GetString();
                    if (!string.IsNullOrEmpty(fileId))
                    {
                        return new Dictionary<string, object>
                        {
                            ["type"] = "photo",
                            ["media"] = fileId
                        };
                    }
                }
            }

            // 检查视频
            if (message.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object)
            {
                if (video.TryGetProperty("file_id", out var fileIdEl))
                {
                    var fileId = fileIdEl.GetString();
                    if (!string.IsNullOrEmpty(fileId))
                    {
                        return new Dictionary<string, object>
                        {
                            ["type"] = "video",
                            ["media"] = fileId
                        };
                    }
                }
            }

            // 检查文档
            if (message.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object)
            {
                if (document.TryGetProperty("file_id", out var fileIdEl))
                {
                    var fileId = fileIdEl.GetString();
                    if (!string.IsNullOrEmpty(fileId))
                    {
                        return new Dictionary<string, object>
                        {
                            ["type"] = "document",
                            ["media"] = fileId
                        };
                    }
                }
            }

            // 检查音频
            if (message.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.Object)
            {
                if (audio.TryGetProperty("file_id", out var fileIdEl))
                {
                    var fileId = fileIdEl.GetString();
                    if (!string.IsNullOrEmpty(fileId))
                    {
                        return new Dictionary<string, object>
                        {
                            ["type"] = "audio",
                            ["media"] = fileId
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提取媒体信息失败");
        }

        return null;
    }

    /// <summary>
    /// 使用 sendMediaGroup API 发送媒体组
    /// </summary>
    private async Task SendMediaGroupAsync(
        string botToken,
        TelegramBotApiClient botApi,
        long chatId,
        List<Dictionary<string, object>> mediaItems,
        CancellationToken cancellationToken)
    {
        // 构建媒体数组 JSON
        var mediaArray = JsonSerializer.Serialize(mediaItems);

        var parameters = new Dictionary<string, string?>
        {
            ["chat_id"] = chatId.ToString(),
            ["media"] = mediaArray
        };

        await botApi.CallAsync(botToken, "sendMediaGroup", parameters, cancellationToken);
    }
}
