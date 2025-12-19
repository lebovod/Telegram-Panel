using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Data;

/// <summary>
/// Data层服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramPanelData(this IServiceCollection services, string connectionString)
    {
        // 注册数据库上下文
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        // 注册所有Repository
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountCategoryRepository, AccountCategoryRepository>();
        services.AddScoped<IAccountChannelRepository, AccountChannelRepository>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IChannelGroupRepository, ChannelGroupRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IBatchTaskRepository, BatchTaskRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        return services;
    }
}
