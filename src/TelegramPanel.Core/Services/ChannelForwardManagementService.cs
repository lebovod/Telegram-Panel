using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services;

/// <summary>
/// 频道转发规则管理服务
/// </summary>
public class ChannelForwardManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ChannelForwardManagementService> _logger;

    public ChannelForwardManagementService(
        AppDbContext dbContext,
        ILogger<ChannelForwardManagementService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有转发规则
    /// </summary>
    public async Task<List<ChannelForwardRule>> GetAllRulesAsync()
    {
        return await _dbContext.ChannelForwardRules
            .Include(r => r.Bot)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 根据机器人ID获取转发规则
    /// </summary>
    public async Task<List<ChannelForwardRule>> GetRulesByBotIdAsync(int botId)
    {
        return await _dbContext.ChannelForwardRules
            .Include(r => r.Bot)
            .Where(r => r.BotId == botId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 获取所有启用的转发规则
    /// </summary>
    public async Task<List<ChannelForwardRule>> GetEnabledRulesAsync()
    {
        return await _dbContext.ChannelForwardRules
            .Include(r => r.Bot)
            .Where(r => r.IsEnabled && r.Bot!.IsActive)
            .ToListAsync();
    }

    /// <summary>
    /// 根据ID获取转发规则
    /// </summary>
    public async Task<ChannelForwardRule?> GetRuleByIdAsync(int id)
    {
        return await _dbContext.ChannelForwardRules
            .Include(r => r.Bot)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>
    /// 创建转发规则
    /// </summary>
    public async Task<ChannelForwardRule> CreateRuleAsync(ChannelForwardRule rule)
    {
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = DateTime.UtcNow;

        _dbContext.ChannelForwardRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("创建转发规则：{RuleName} (ID: {RuleId})", rule.Name, rule.Id);
        return rule;
    }

    /// <summary>
    /// 更新转发规则
    /// </summary>
    public async Task<bool> UpdateRuleAsync(ChannelForwardRule rule)
    {
        var existing = await _dbContext.ChannelForwardRules.FindAsync(rule.Id);
        if (existing == null)
        {
            return false;
        }

        _logger.LogInformation("更新前 - IsEnabled: {Before}, 更新后: {After}", existing.IsEnabled, rule.IsEnabled);
        _logger.LogInformation("更新前 - DeleteLinks: {Before}, 更新后: {After}", existing.DeleteLinks, rule.DeleteLinks);
        _logger.LogInformation("更新前 - DeleteMentions: {Before}, 更新后: {After}", existing.DeleteMentions, rule.DeleteMentions);

        existing.Name = rule.Name;
        existing.BotId = rule.BotId;
        existing.SourceChannelId = rule.SourceChannelId;
        existing.SourceChannelUsername = rule.SourceChannelUsername;
        existing.SourceChannelTitle = rule.SourceChannelTitle;
        existing.TargetChannelIds = rule.TargetChannelIds;
        existing.IsEnabled = rule.IsEnabled;
        existing.FooterTemplate = rule.FooterTemplate;
        existing.DeleteAfterKeywords = rule.DeleteAfterKeywords;
        existing.DeletePatterns = rule.DeletePatterns;
        existing.DeleteLinks = rule.DeleteLinks;
        existing.DeleteMentions = rule.DeleteMentions;
        existing.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("更新转发规则：{RuleName} (ID: {RuleId}), IsEnabled={IsEnabled}, DeleteLinks={DeleteLinks}, DeleteMentions={DeleteMentions}", 
            rule.Name, rule.Id, existing.IsEnabled, existing.DeleteLinks, existing.DeleteMentions);
        return true;
    }

    /// <summary>
    /// 删除转发规则
    /// </summary>
    public async Task<bool> DeleteRuleAsync(int id)
    {
        var rule = await _dbContext.ChannelForwardRules.FindAsync(id);
        if (rule == null)
        {
            return false;
        }

        _dbContext.ChannelForwardRules.Remove(rule);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("删除转发规则：{RuleName} (ID: {RuleId})", rule.Name, id);
        return true;
    }

    /// <summary>
    /// 切换规则启用状态
    /// </summary>
    public async Task<bool> ToggleRuleStatusAsync(int id)
    {
        var rule = await _dbContext.ChannelForwardRules.FindAsync(id);
        if (rule == null)
        {
            return false;
        }

        rule.IsEnabled = !rule.IsEnabled;
        rule.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("切换转发规则状态：{RuleName} (ID: {RuleId}), 新状态: {Status}",
            rule.Name, id, rule.IsEnabled ? "启用" : "禁用");
        return true;
    }

    /// <summary>
    /// 更新规则统计信息
    /// </summary>
    public async Task UpdateRuleStatsAsync(int ruleId, int? lastMessageId, bool forwarded)
    {
        var rule = await _dbContext.ChannelForwardRules.FindAsync(ruleId);
        if (rule == null)
        {
            return;
        }

        rule.LastProcessedAt = DateTime.UtcNow;
        if (lastMessageId.HasValue)
        {
            rule.LastProcessedMessageId = lastMessageId.Value;
        }

        if (forwarded)
        {
            rule.ForwardedCount++;
        }
        else
        {
            rule.SkippedCount++;
        }

        await _dbContext.SaveChangesAsync();
    }
}

