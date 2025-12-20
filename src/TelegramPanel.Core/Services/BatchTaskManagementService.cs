using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Core.Services;

/// <summary>
/// 批量任务管理服务
/// </summary>
public class BatchTaskManagementService
{
    private readonly IBatchTaskRepository _batchTaskRepository;

    public BatchTaskManagementService(IBatchTaskRepository batchTaskRepository)
    {
        _batchTaskRepository = batchTaskRepository;
    }

    public async Task<BatchTask?> GetTaskAsync(int id)
    {
        return await _batchTaskRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<BatchTask>> GetAllTasksAsync()
    {
        return await _batchTaskRepository.GetAllAsync();
    }

    public async Task<IEnumerable<BatchTask>> GetTasksByStatusAsync(string status)
    {
        return await _batchTaskRepository.GetByStatusAsync(status);
    }

    public async Task<IEnumerable<BatchTask>> GetRunningTasksAsync()
    {
        return await _batchTaskRepository.GetRunningTasksAsync();
    }

    public async Task<IEnumerable<BatchTask>> GetRecentTasksAsync(int count = 20)
    {
        return await _batchTaskRepository.GetRecentTasksAsync(count);
    }

    public async Task<BatchTask> CreateTaskAsync(BatchTask task)
    {
        task.CreatedAt = DateTime.UtcNow;
        task.Status = "pending";
        return await _batchTaskRepository.AddAsync(task);
    }

    public async Task UpdateTaskProgressAsync(int taskId, int completed, int failed)
    {
        var task = await _batchTaskRepository.GetByIdAsync(taskId);
        if (task != null)
        {
            task.Completed = completed;
            task.Failed = failed;
            await _batchTaskRepository.UpdateAsync(task);
        }
    }

    public async Task UpdateTaskConfigAsync(int taskId, string? config)
    {
        var task = await _batchTaskRepository.GetByIdAsync(taskId);
        if (task != null)
        {
            task.Config = config;
            await _batchTaskRepository.UpdateAsync(task);
        }
    }

    public async Task StartTaskAsync(int taskId)
    {
        var task = await _batchTaskRepository.GetByIdAsync(taskId);
        if (task != null)
        {
            task.Status = "running";
            task.StartedAt = DateTime.UtcNow;
            await _batchTaskRepository.UpdateAsync(task);
        }
    }

    public async Task CompleteTaskAsync(int taskId, bool success = true)
    {
        var task = await _batchTaskRepository.GetByIdAsync(taskId);
        if (task != null)
        {
            task.Status = success ? "completed" : "failed";
            task.CompletedAt = DateTime.UtcNow;
            await _batchTaskRepository.UpdateAsync(task);
        }
    }

    public async Task DeleteTaskAsync(int id)
    {
        var task = await _batchTaskRepository.GetByIdAsync(id);
        if (task != null)
        {
            await _batchTaskRepository.DeleteAsync(task);
        }
    }
}
