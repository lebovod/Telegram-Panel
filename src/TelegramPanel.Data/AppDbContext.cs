using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data;

/// <summary>
/// 应用程序数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // 实体集合
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountCategory> AccountCategories => Set<AccountCategory>();
    public DbSet<AccountChannel> AccountChannels => Set<AccountChannel>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelGroup> ChannelGroups => Set<ChannelGroup>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<BatchTask> BatchTasks => Set<BatchTask>();
    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<BotChannel> BotChannels => Set<BotChannel>();
    public DbSet<BotChannelCategory> BotChannelCategories => Set<BotChannelCategory>();
    public DbSet<ChannelForwardRule> ChannelForwardRules => Set<ChannelForwardRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Account配置
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(20);
            entity.Property(e => e.SessionPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ApiHash).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.Nickname).HasMaxLength(100);

            entity.Property(e => e.TelegramStatusSummary).HasMaxLength(200);
            entity.Property(e => e.TelegramStatusDetails).HasMaxLength(2000);

            entity.HasIndex(e => e.Phone).IsUnique();
            // 注意：UserId 在未登录/未验证的 session 导入场景可能为 0，SQLite 的 UNIQUE 约束会导致多账号导入失败。
            // 这里保留索引但不做唯一约束，真正的去重以 Phone 为准。
            entity.HasIndex(e => e.UserId);

            // 与AccountCategory的关系
            entity.HasOne(e => e.Category)
                .WithMany(c => c.Accounts)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AccountCategory配置
        modelBuilder.Entity<AccountCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Channel配置
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.About).HasMaxLength(1000);

            entity.HasIndex(e => e.TelegramId).IsUnique();
            entity.HasIndex(e => e.Username);

            // 与Account的关系（创建者）
            entity.HasOne(e => e.CreatorAccount)
                .WithMany(a => a.Channels)
                .HasForeignKey(e => e.CreatorAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            // 与ChannelGroup的关系
            entity.HasOne(e => e.Group)
                .WithMany(g => g.Channels)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AccountChannel配置
        modelBuilder.Entity<AccountChannel>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.AccountId, e.ChannelId }).IsUnique();
            entity.HasIndex(e => e.ChannelId);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.AccountChannels)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Channel)
                .WithMany(c => c.AccountChannels)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ChannelGroup配置
        modelBuilder.Entity<ChannelGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Group配置
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.About).HasMaxLength(1000);

            entity.HasIndex(e => e.TelegramId).IsUnique();
            entity.HasIndex(e => e.Username);

            // 与Account的关系（创建者）
            entity.HasOne(e => e.CreatorAccount)
                .WithMany(a => a.Groups)
                .HasForeignKey(e => e.CreatorAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BatchTask配置
        modelBuilder.Entity<BatchTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Bot配置
        modelBuilder.Entity<Bot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Username).HasMaxLength(100);

            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Username);
        });

        // BotChannelCategory配置
        modelBuilder.Entity<BotChannelCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasIndex(e => new { e.BotId, e.Name }).IsUnique();

            entity.HasOne(e => e.Bot)
                .WithMany(b => b.Categories)
                .HasForeignKey(e => e.BotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BotChannel配置
        modelBuilder.Entity<BotChannel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.About).HasMaxLength(1000);

            entity.HasIndex(e => new { e.BotId, e.TelegramId }).IsUnique();
            entity.HasIndex(e => e.Username);
            entity.HasIndex(e => e.CategoryId);

            entity.HasOne(e => e.Bot)
                .WithMany(b => b.Channels)
                .HasForeignKey(e => e.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Channels)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChannelForwardRule配置
        modelBuilder.Entity<ChannelForwardRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.TargetChannelIds).IsRequired();

            entity.HasIndex(e => e.BotId);
            entity.HasIndex(e => e.SourceChannelId);
            entity.HasIndex(e => e.IsEnabled);

            entity.HasOne(e => e.Bot)
                .WithMany()
                .HasForeignKey(e => e.BotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
