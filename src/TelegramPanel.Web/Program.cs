using MudBlazor.Services;
using Serilog;
using TelegramPanel.Core;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/telegram-panel-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor
builder.Services.AddMudServices();

// Telegram Panel 核心服务
builder.Services.AddTelegramPanelCore();

// TODO: 添加数据库上下文
// builder.Services.AddDbContext<AppDbContext>(options =>
//     options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// TODO: 添加 Hangfire
// builder.Services.AddHangfire(config => config.UseInMemoryStorage());
// builder.Services.AddHangfireServer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// 静态文件（包括 MudBlazor 等 NuGet 包的静态 Web 资源）
app.UseStaticFiles();

app.UseAntiforgery();

// Serilog 请求日志
app.UseSerilogRequestLogging();

app.MapRazorComponents<TelegramPanel.Web.Components.App>()
    .AddInteractiveServerRenderMode();

// TODO: Hangfire Dashboard
// app.MapHangfireDashboard("/hangfire");

Log.Information("Telegram Panel started");

app.Run();
