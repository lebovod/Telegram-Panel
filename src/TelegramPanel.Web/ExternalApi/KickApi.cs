using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Web.ExternalApi;

public static class KickApi
{
    public static void MapKickApi(this WebApplication app)
    {
        app.MapPost("/api/kick", HandleAsync)
            .DisableAntiforgery()
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IConfiguration configuration,
        BotManagementService botManagement,
        BotTelegramService botTelegram,
        BatchTaskManagementService taskManagement,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ExternalApi.Kick");
        var providedKey = http.Request.Headers["X-API-Key"].ToString();

        var apis = configuration.GetSection("ExternalApi:Apis").Get<List<ExternalApiDefinition>>() ?? new List<ExternalApiDefinition>();
        var kickApis = apis.Where(a => string.Equals(a.Type, ExternalApiTypes.Kick, StringComparison.OrdinalIgnoreCase)).ToList();
        var matched = kickApis.FirstOrDefault(a => a.Enabled && FixedTimeEquals(a.ApiKey, providedKey));
        if (matched == null)
        {
            var anyEnabled = kickApis.Any(a => a.Enabled);
            if (!anyEnabled)
            {
                return Results.NotFound(new KickResponse(
                    false,
                    "该接口未启用（请先在面板「API 管理」创建并启用一个 /api/kick 配置）",
                    new KickSummary(0, 0, 0),
                    Array.Empty<KickResultItem>()));
            }

            return Results.Json(
                new KickResponse(false, "X-API-Key 无效", new KickSummary(0, 0, 0), Array.Empty<KickResultItem>()),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        KickRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<KickRequest>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Results.BadRequest(new KickResponse(false, $"请求体 JSON 无效：{ex.Message}", new KickSummary(0, 0, 0), Array.Empty<KickResultItem>()));
        }

        if (request == null)
            return Results.BadRequest(new KickResponse(false, "请求体不能为空", new KickSummary(0, 0, 0), Array.Empty<KickResultItem>()));

        if (request.UserId <= 0)
            return Results.BadRequest(new KickResponse(false, "user_id 无效", new KickSummary(0, 0, 0), Array.Empty<KickResultItem>()));

        var permanentBan = request.PermanentBan ?? matched.Kick.PermanentBanDefault;
        var configuredBotId = matched.Kick.BotId;
        var useAllChats = configuredBotId == 0 ? true : matched.Kick.UseAllChats;
        var configuredChatSet = new HashSet<long>(matched.Kick.ChatIds.Where(x => x != 0));

        var targets = await ResolveTargetsAsync(botManagement, configuredBotId, useAllChats, configuredChatSet, cancellationToken);
        if (targets.TotalChats == 0)
        {
            return Results.BadRequest(new KickResponse(
                false,
                "未配置任何可操作的频道/群组（请先在页面选择机器人与频道/群组）",
                new KickSummary(0, 0, 0),
                Array.Empty<KickResultItem>()));
        }

        // 记录到任务中心（即使外部调用返回了，也能追溯这次操作）
        var createdTask = await taskManagement.CreateTaskAsync(new BatchTask
        {
            TaskType = BatchTaskTypes.ExternalApiKick,
            Total = targets.TotalChats,
            Completed = 0,
            Failed = 0,
            Config = SerializeIndented(new KickTaskLog
            {
                ApiName = matched.Name ?? "",
                BotId = configuredBotId,
                UseAllChats = useAllChats,
                ChatIds = configuredChatSet.OrderBy(x => x).ToList(),
                UserId = request.UserId,
                PermanentBan = permanentBan,
                RequestedAtUtc = DateTime.UtcNow
            })
        });

        await taskManagement.StartTaskAsync(createdTask.Id);

        var results = new List<KickResultItem>(targets.TotalChats);
        var completed = 0;
        var failedCount = 0;

        try
        {
            foreach (var group in targets.Groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var banResult = await botTelegram.BanChatMemberAsync(
                        botId: group.Bot.Id,
                        channelTelegramIds: group.Chats.Select(x => x.TelegramId).ToList(),
                        userId: request.UserId,
                        permanentBan: permanentBan,
                        cancellationToken: cancellationToken);

                    var failures = banResult.Failures;
                    foreach (var chat in group.Chats)
                    {
                        failures.TryGetValue(chat.TelegramId, out var err);
                        var ok = err == null;
                        results.Add(new KickResultItem(
                            ChatId: chat.TelegramId.ToString(),
                            Title: chat.Title,
                            Success: ok,
                            Error: err));

                        if (ok) completed++; else failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Kick API failed on bot {BotId} (chats={Count})", group.Bot.Id, group.Chats.Count);
                    foreach (var chat in group.Chats)
                    {
                        results.Add(new KickResultItem(
                            ChatId: chat.TelegramId.ToString(),
                            Title: chat.Title,
                            Success: false,
                            Error: ex.Message));
                        failedCount++;
                    }
                }

                await taskManagement.UpdateTaskProgressAsync(createdTask.Id, completed, failedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await taskManagement.UpdateTaskProgressAsync(createdTask.Id, completed, failedCount == 0 ? 1 : failedCount);
            await taskManagement.UpdateTaskConfigAsync(createdTask.Id, SerializeIndented(new KickTaskLog
            {
                ApiName = matched.Name ?? "",
                BotId = configuredBotId,
                UseAllChats = useAllChats,
                ChatIds = configuredChatSet.OrderBy(x => x).ToList(),
                UserId = request.UserId,
                PermanentBan = permanentBan,
                RequestedAtUtc = DateTime.UtcNow,
                Results = results,
                Canceled = true
            }));
            await taskManagement.CompleteTaskAsync(createdTask.Id, success: false);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kick API failed (taskId={TaskId})", createdTask.Id);
            await taskManagement.UpdateTaskProgressAsync(createdTask.Id, completed, failedCount == 0 ? 1 : failedCount);
            await taskManagement.UpdateTaskConfigAsync(createdTask.Id, SerializeIndented(new KickTaskLog
            {
                ApiName = matched.Name ?? "",
                BotId = configuredBotId,
                UseAllChats = useAllChats,
                ChatIds = configuredChatSet.OrderBy(x => x).ToList(),
                UserId = request.UserId,
                PermanentBan = permanentBan,
                RequestedAtUtc = DateTime.UtcNow,
                Results = results,
                Error = ex.Message
            }));
            await taskManagement.CompleteTaskAsync(createdTask.Id, success: false);
            throw;
        }

        var okCount = results.Count(x => x.Success);
        var total = results.Count;
        var failedTotal = total - okCount;
        var actionText = permanentBan ? "Banned" : "Kicked";
        var message = $"{actionText} user {request.UserId} from {okCount}/{total} chats";

        await taskManagement.UpdateTaskConfigAsync(createdTask.Id, SerializeIndented(new KickTaskLog
        {
            ApiName = matched.Name ?? "",
            BotId = configuredBotId,
            UseAllChats = useAllChats,
            ChatIds = configuredChatSet.OrderBy(x => x).ToList(),
            UserId = request.UserId,
            PermanentBan = permanentBan,
            RequestedAtUtc = DateTime.UtcNow,
            Results = results
        }));
        await taskManagement.CompleteTaskAsync(createdTask.Id, success: failedTotal == 0);

        return Results.Ok(new KickResponse(
            Success: true,
            Message: message,
            Summary: new KickSummary(total, okCount, failedTotal),
            Results: results));
    }

    private static async Task<TargetResolution> ResolveTargetsAsync(
        BotManagementService botManagement,
        int configuredBotId,
        bool useAllChats,
        HashSet<long> configuredChatIds,
        CancellationToken cancellationToken)
    {
        var bots = (await botManagement.GetAllBotsAsync())
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .ToList();

        if (configuredBotId > 0)
            bots = bots.Where(b => b.Id == configuredBotId).ToList();

        var groups = new List<TargetGroup>();
        var totalChats = 0;

        foreach (var bot in bots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chats = (await botManagement.GetChatsAsync(bot.Id)).ToList();
            if (!useAllChats && configuredChatIds.Count > 0)
                chats = chats.Where(c => configuredChatIds.Contains(c.TelegramId)).ToList();

            if (chats.Count == 0)
                continue;

            groups.Add(new TargetGroup(bot, chats));
            totalChats += chats.Count;
        }

        return new TargetResolution(groups, totalChats);
    }

    private static bool FixedTimeEquals(string expected, string provided)
    {
        expected = (expected ?? string.Empty).Trim();
        provided = (provided ?? string.Empty).Trim();

        if (expected.Length == 0 || provided.Length == 0)
            return false;

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(provided);
        if (a.Length != b.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string SerializeIndented<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }

    public sealed record KickRequest(
        [property: JsonPropertyName("user_id")] long UserId,
        [property: JsonPropertyName("permanent_ban")] bool? PermanentBan = null);

    public sealed record KickResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("summary")] KickSummary Summary,
        [property: JsonPropertyName("results")] IReadOnlyList<KickResultItem> Results);

    public sealed record KickSummary(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("success")] int Success,
        [property: JsonPropertyName("failed")] int Failed);

    public sealed record KickResultItem(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record TargetGroup(Bot Bot, List<BotChannel> Chats);
    private sealed record TargetResolution(List<TargetGroup> Groups, int TotalChats);

    private sealed class KickTaskLog
    {
        public string ApiName { get; set; } = "";
        public int BotId { get; set; }
        public bool UseAllChats { get; set; }
        public List<long> ChatIds { get; set; } = new();
        public long UserId { get; set; }
        public bool PermanentBan { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public bool Canceled { get; set; }
        public string? Error { get; set; }
        public List<KickResultItem>? Results { get; set; }
    }
}
