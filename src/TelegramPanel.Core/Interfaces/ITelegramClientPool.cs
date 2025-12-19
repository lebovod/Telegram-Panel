using WTelegram;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// Telegram客户端池接口
/// 管理多个Telegram账号的客户端实例
/// </summary>
public interface ITelegramClientPool
{
    /// <summary>
    /// 获取或创建指定账号的客户端
    /// </summary>
    Task<Client> GetOrCreateClientAsync(int accountId, int apiId, string apiHash, string sessionPath, string? phoneNumber = null, long? userId = null);

    /// <summary>
    /// 获取已存在的客户端
    /// </summary>
    Client? GetClient(int accountId);

    /// <summary>
    /// 移除并断开客户端连接
    /// </summary>
    Task RemoveClientAsync(int accountId);

    /// <summary>
    /// 获取所有活跃的客户端数量
    /// </summary>
    int ActiveClientCount { get; }

    /// <summary>
    /// 检查客户端是否已连接
    /// </summary>
    bool IsClientConnected(int accountId);
}
