using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using WTelegram;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// Telegram客户端池管理
/// 负责管理多个Telegram账号的客户端实例
/// </summary>
public class TelegramClientPool : ITelegramClientPool, IDisposable
{
    private readonly ConcurrentDictionary<int, Client> _clients = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    private readonly ILogger<TelegramClientPool> _logger;
    private bool _disposed;

    public TelegramClientPool(ILogger<TelegramClientPool> logger)
    {
        _logger = logger;
    }

    public int ActiveClientCount => _clients.Count;

    public async Task<Client> GetOrCreateClientAsync(int accountId, int apiId, string apiHash, string sessionPath, string? phoneNumber = null, long? userId = null)
    {
        if (_clients.TryGetValue(accountId, out var existingClient))
        {
            if (existingClient.User != null)
            {
                return existingClient;
            }
            // 客户端存在但未登录，移除并重新创建
            await RemoveClientAsync(accountId);
        }

        var lockObj = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();

        try
        {
            // 双重检查
            if (_clients.TryGetValue(accountId, out existingClient) && existingClient.User != null)
            {
                return existingClient;
            }

            _logger.LogInformation("Creating new Telegram client for account {AccountId}", accountId);

            // 使用 config 回调设置 session 路径
            phoneNumber = NormalizePhone(phoneNumber);
            string Config(string what)
            {
                return what switch
                {
                    "api_id" => apiId.ToString(),
                    "api_hash" => apiHash,
                    "session_pathname" => sessionPath,
                    "phone_number" => string.IsNullOrWhiteSpace(phoneNumber) ? null! : phoneNumber,
                    "user_id" => userId.HasValue && userId.Value > 0 ? userId.Value.ToString() : null!,
                    _ => null!  // 使用 null! 抑制警告，这是 WTelegramClient 的预期行为
                };
            }

            var client = new Client(Config);

            // 设置日志回调
            client.OnOther += (update) =>
            {
                _logger.LogDebug("Account {AccountId} received update: {Update}", accountId, update.GetType().Name);
                return Task.CompletedTask;
            };

            _clients[accountId] = client;
            return client;
        }
        finally
        {
            lockObj.Release();
        }
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var digits = new char[phone.Length];
        var count = 0;
        foreach (var ch in phone)
        {
            if (ch >= '0' && ch <= '9')
                digits[count++] = ch;
        }
        return count == 0 ? null : new string(digits, 0, count);
    }

    public Client? GetClient(int accountId)
    {
        return _clients.TryGetValue(accountId, out var client) ? client : null;
    }

    public async Task RemoveClientAsync(int accountId)
    {
        if (_clients.TryRemove(accountId, out var client))
        {
            _logger.LogInformation("Removing Telegram client for account {AccountId}", accountId);

            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing client for account {AccountId}", accountId);
            }
        }
    }

    public bool IsClientConnected(int accountId)
    {
        return _clients.TryGetValue(accountId, out var client) && client.User != null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var client in _clients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
                // 忽略清理时的错误
            }
        }

        _clients.Clear();

        foreach (var lockObj in _locks.Values)
        {
            lockObj.Dispose();
        }

        _locks.Clear();

        GC.SuppressFinalize(this);
    }
}
