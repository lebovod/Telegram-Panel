namespace TelegramPanel.Core.Models;

/// <summary>
/// 批量任务信息
/// </summary>
public record BatchTaskInfo
{
    public int Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TaskStatus Status { get; init; }
    public int Progress { get; init; }
    public int Total { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    public double ProgressPercent => Total > 0 ? (double)Progress / Total * 100 : 0;
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}

/// <summary>
/// 任务状态
/// </summary>
public enum TaskStatus
{
    Pending,    // 等待中
    Running,    // 执行中
    Completed,  // 已完成
    Failed,     // 失败
    Cancelled   // 已取消
}

/// <summary>
/// 任务类型
/// </summary>
public static class TaskTypes
{
    public const string InviteUsers = "invite_users";
    public const string SetAdmins = "set_admins";
    public const string CreateChannel = "create_channel";
    public const string SyncData = "sync_data";
}
