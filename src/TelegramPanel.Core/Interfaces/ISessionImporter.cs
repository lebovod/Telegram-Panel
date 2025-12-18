using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// Session导入服务接口
/// </summary>
public interface ISessionImporter
{
    /// <summary>
    /// 从Session文件导入
    /// </summary>
    Task<ImportResult> ImportFromSessionFileAsync(string filePath, int apiId, string apiHash);

    /// <summary>
    /// 批量导入Session文件
    /// </summary>
    Task<List<ImportResult>> BatchImportSessionFilesAsync(string[] filePaths, int apiId, string apiHash);

    /// <summary>
    /// 从StringSession导入
    /// </summary>
    Task<ImportResult> ImportFromStringSessionAsync(string sessionString, int apiId, string apiHash);

    /// <summary>
    /// 验证Session是否有效
    /// </summary>
    Task<bool> ValidateSessionAsync(string sessionPath);
}

/// <summary>
/// 导入结果
/// </summary>
public record ImportResult(
    bool Success,
    string? Phone,
    long? UserId,
    string? Username,
    string? SessionPath,
    string? Error = null
);
