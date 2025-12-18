using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using WTelegram;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// Session导入服务实现
/// </summary>
public class SessionImporter : ISessionImporter
{
    private readonly ILogger<SessionImporter> _logger;

    public SessionImporter(ILogger<SessionImporter> logger)
    {
        _logger = logger;
    }

    public async Task<ImportResult> ImportFromSessionFileAsync(string filePath, int apiId, string apiHash)
    {
        if (!File.Exists(filePath))
        {
            return new ImportResult(false, null, null, null, null, $"Session file not found: {filePath}");
        }

        try
        {
            _logger.LogInformation("Importing session from file: {FilePath}", filePath);

            // 复制到sessions目录
            var fileName = Path.GetFileName(filePath);
            var targetPath = Path.Combine("sessions", fileName);

            if (!Directory.Exists("sessions"))
            {
                Directory.CreateDirectory("sessions");
            }

            File.Copy(filePath, targetPath, overwrite: true);

            // 使用 config 回调设置 session 路径
            string Config(string what) => what switch
            {
                "api_id" => apiId.ToString(),
                "api_hash" => apiHash,
                "session_pathname" => targetPath,
                _ => null!
            };

            using var client = new Client(Config);
            await client.ConnectAsync();

            if (client.User != null)
            {
                _logger.LogInformation("Session imported successfully for user {UserId}", client.User.id);

                return new ImportResult(
                    Success: true,
                    Phone: client.User.phone,
                    UserId: client.User.id,
                    Username: client.User.MainUsername,
                    SessionPath: targetPath
                );
            }

            return new ImportResult(false, null, null, null, targetPath, "Session exists but user not logged in");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import session from {FilePath}", filePath);
            return new ImportResult(false, null, null, null, null, ex.Message);
        }
    }

    public async Task<List<ImportResult>> BatchImportSessionFilesAsync(string[] filePaths, int apiId, string apiHash)
    {
        var results = new List<ImportResult>();

        foreach (var filePath in filePaths)
        {
            var result = await ImportFromSessionFileAsync(filePath, apiId, apiHash);
            results.Add(result);

            // 短暂延迟避免频繁连接
            await Task.Delay(500);
        }

        var successCount = results.Count(r => r.Success);
        _logger.LogInformation("Batch import completed: {Success}/{Total} successful", successCount, results.Count);

        return results;
    }

    public async Task<ImportResult> ImportFromStringSessionAsync(string sessionString, int apiId, string apiHash)
    {
        try
        {
            // WTelegramClient 使用二进制session文件，不直接支持StringSession
            // 需要将base64字符串解码并保存为文件

            var sessionData = Convert.FromBase64String(sessionString);
            var sessionPath = Path.Combine("sessions", $"{Guid.NewGuid()}.session");

            if (!Directory.Exists("sessions"))
            {
                Directory.CreateDirectory("sessions");
            }

            await File.WriteAllBytesAsync(sessionPath, sessionData);

            // 使用 config 回调设置 session 路径
            string Config(string what) => what switch
            {
                "api_id" => apiId.ToString(),
                "api_hash" => apiHash,
                "session_pathname" => sessionPath,
                _ => null!
            };

            using var client = new Client(Config);
            await client.ConnectAsync();

            if (client.User != null)
            {
                // 重命名为手机号
                var newPath = Path.Combine("sessions", $"{client.User.phone}.session");
                File.Move(sessionPath, newPath, overwrite: true);

                return new ImportResult(
                    Success: true,
                    Phone: client.User.phone,
                    UserId: client.User.id,
                    Username: client.User.MainUsername,
                    SessionPath: newPath
                );
            }

            // 删除无效session
            File.Delete(sessionPath);
            return new ImportResult(false, null, null, null, null, "Invalid session string");
        }
        catch (FormatException)
        {
            return new ImportResult(false, null, null, null, null, "Invalid base64 format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from string session");
            return new ImportResult(false, null, null, null, null, ex.Message);
        }
    }

    public Task<bool> ValidateSessionAsync(string sessionPath)
    {
        if (!File.Exists(sessionPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            // 简单检查文件大小（有效session通常大于0字节）
            var fileInfo = new FileInfo(sessionPath);
            return Task.FromResult(fileInfo.Length > 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
