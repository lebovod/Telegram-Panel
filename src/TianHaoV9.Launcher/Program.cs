using System.Diagnostics;
using System.Net.Sockets;

const string appName = "天浩独家开发 V9";
const string webExeName = "TelegramPanel.Web.exe";
const string localUrl = "http://127.0.0.1:5188";

var baseDirectory = AppContext.BaseDirectory;
var logDirectory = Path.Combine(baseDirectory, "logs");
var launcherLog = Path.Combine(logDirectory, "launcher.log");

Directory.CreateDirectory(logDirectory);

void WriteLog(string message)
{
    try
    {
        File.AppendAllText(
            launcherLog,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
    }
    catch
    {
        // 启动器日志失败不能阻塞主程序启动。
    }
}

bool IsPortOpen()
{
    try
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync("127.0.0.1", 5188);
        return connectTask.Wait(TimeSpan.FromMilliseconds(350)) && client.Connected;
    }
    catch
    {
        return false;
    }
}

void OpenBrowser()
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = localUrl,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        WriteLog($"打开浏览器失败：{ex}");
    }
}

try
{
    using var mutex = new Mutex(true, "Local\\TianHaoV9Launcher", out var createdNew);

    if (IsPortOpen())
    {
        WriteLog("检测到服务已运行，直接打开浏览器。");
        OpenBrowser();
        return;
    }

    if (!createdNew)
    {
        WriteLog("检测到另一个启动器正在启动服务，等待服务就绪。");
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(250);
            if (IsPortOpen())
            {
                OpenBrowser();
                return;
            }
        }
    }

    var webExePath = Path.Combine(baseDirectory, webExeName);
    if (!File.Exists(webExePath))
    {
        WriteLog($"缺少主程序：{webExePath}");
        MessageBox.Show(
            $"未找到主程序文件：{webExeName}\n请重新安装 {appName}。",
            appName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = webExePath,
        WorkingDirectory = baseDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };

    startInfo.Environment["ASPNETCORE_URLS"] = localUrl;
    startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

    var process = Process.Start(startInfo);
    if (process is null)
        throw new InvalidOperationException("系统未能启动主程序进程。");

    WriteLog($"主程序已启动，PID={process.Id}。");

    for (var i = 0; i < 80; i++)
    {
        Thread.Sleep(250);

        if (process.HasExited)
        {
            WriteLog($"主程序启动后退出，ExitCode={process.ExitCode}。");
            MessageBox.Show(
                $"{appName} 启动失败。\n请查看 logs 目录中的日志。",
                appName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (IsPortOpen())
        {
            WriteLog("服务已就绪，打开浏览器。");
            OpenBrowser();
            return;
        }
    }

    WriteLog("等待服务启动超时。");
    MessageBox.Show(
        $"{appName} 启动超时。\n程序可能仍在后台初始化，请稍后再次双击桌面图标。",
        appName,
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
}
catch (Exception ex)
{
    WriteLog($"启动器异常：{ex}");
    MessageBox.Show(
        $"{appName} 无法启动。\n{ex.Message}\n\n详细信息已写入 logs\\launcher.log。",
        appName,
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
