using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services.Telegram;

namespace TelegramPanel.Core;

/// <summary>
/// Core层服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramPanelCore(this IServiceCollection services)
    {
        // 注册 Telegram 客户端池（单例）
        services.AddSingleton<ITelegramClientPool, TelegramClientPool>();

        // 注册账号服务
        services.AddScoped<IAccountService, AccountService>();

        // 注册频道服务
        services.AddScoped<IChannelService, ChannelService>();

        // 注册群组服务
        services.AddScoped<IGroupService, GroupService>();

        // 注册 Session 导入服务
        services.AddScoped<ISessionImporter, SessionImporter>();

        return services;
    }
}
