using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TelegramPanel.Web.Services;

public static class LocalConfigFile
{
    public static string ResolvePath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = (configuration["LocalConfig:Path"] ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        // Docker 部署默认持久化目录为 /data（docker-compose 挂载 ./docker-data:/data）
        // 即便没有显式配置，也优先写到 /data，避免写入镜像层 /app 导致丢失或权限问题。
        if (Directory.Exists("/data"))
            return "/data/appsettings.local.json";

        return Path.Combine(environment.ContentRootPath, "appsettings.local.json");
    }

    public static async Task EnsureExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(path))
            return;

        await File.WriteAllTextAsync(path, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    public static async Task WriteJsonAtomicallyAsync(string path, string json, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = $"{path}.tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        File.Move(tmp, path, overwrite: true);
    }
}

