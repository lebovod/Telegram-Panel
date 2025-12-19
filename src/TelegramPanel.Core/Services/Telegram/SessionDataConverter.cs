using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TL;
using WTelegram;

namespace TelegramPanel.Core.Services.Telegram;

internal static class SessionDataConverter
{
    public static async Task<bool> TryConvertSqliteSessionFromJsonAsync(
        string phone,
        int apiId,
        string apiHash,
        string sqliteSessionPath,
        ILogger logger)
    {
        try
        {
            var absoluteSqliteSessionPath = Path.GetFullPath(sqliteSessionPath);
            var jsonPath = TryFindAnySessionJsonPath(phone, absoluteSqliteSessionPath);
            if (string.IsNullOrWhiteSpace(jsonPath))
                return false;

            var jsonText = await File.ReadAllTextAsync(jsonPath);
            using var doc = JsonDocument.Parse(jsonText);

            if (!doc.RootElement.TryGetProperty("session_string", out var sessionProp) || sessionProp.ValueKind != JsonValueKind.String)
                return false;

            var sessionString = sessionProp.GetString();
            if (string.IsNullOrWhiteSpace(sessionString))
                return false;

            _ = doc.RootElement.TryGetProperty("user_id", out var userIdProp);
            _ = doc.RootElement.TryGetProperty("uid", out var uidProp);
            long? userId = null;
            if (userIdProp.ValueKind == JsonValueKind.Number && userIdProp.TryGetInt64(out var uid1)) userId = uid1;
            if (userId == null && uidProp.ValueKind == JsonValueKind.Number && uidProp.TryGetInt64(out var uid2)) userId = uid2;

            var ok = await TryCreateWTelegramSessionFromSessionStringAsync(
                sessionString: sessionString.Trim(),
                apiId: apiId,
                apiHash: apiHash,
                targetSessionPath: absoluteSqliteSessionPath,
                phone: phone,
                userId: userId,
                logger: logger);
            if (!ok) return false;

            logger.LogInformation("Converted sqlite session for {Phone} using json: {JsonPath}", phone, jsonPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to convert sqlite session for {Phone} from json", phone);
            return false;
        }
    }

    public static async Task<bool> TryCreateWTelegramSessionFromSessionStringAsync(
        string sessionString,
        int apiId,
        string apiHash,
        string targetSessionPath,
        string phone,
        long? userId,
        ILogger logger)
    {
        string? backupPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(sessionString))
                return false;

            var absoluteTargetSessionPath = Path.GetFullPath(targetSessionPath);
            var normalizedPhone = NormalizePhone(phone);
            if (string.IsNullOrWhiteSpace(normalizedPhone))
                normalizedPhone = NormalizePhone(Path.GetFileNameWithoutExtension(absoluteTargetSessionPath));

            if (!TryParseTelethonStringSession(sessionString.Trim(), out var telethon))
                return false;

            // 先备份旧 sqlite session，再生成 WTelegram session 覆盖原路径
            if (File.Exists(absoluteTargetSessionPath))
            {
                var suffix = LooksLikeSqliteSession(absoluteTargetSessionPath) ? "sqlite.bak" : "bak";
                backupPath = BuildBackupPath(absoluteTargetSessionPath, suffix);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? Directory.GetCurrentDirectory());
                File.Move(absoluteTargetSessionPath, backupPath, overwrite: true);
            }

            var sessionsDir = Path.GetDirectoryName(absoluteTargetSessionPath) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(sessionsDir);

            // 使用 WTelegram 的 Session 存储格式生成可用 session 文件（加密 JSON）
            var ok = await WriteWTelegramSessionFileAsync(
                apiId: apiId,
                apiHash: apiHash,
                sessionPath: absoluteTargetSessionPath,
                phoneDigits: normalizedPhone,
                userId: userId,
                dcId: telethon.DcId,
                ipAddress: telethon.IpAddress,
                port: telethon.Port,
                authKey: telethon.AuthKey,
                logger: logger
            );
            if (!ok)
                throw new InvalidOperationException("WTelegram session 生成失败或校验失败");

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create WTelegram session from session_string");
            try { if (File.Exists(targetSessionPath)) File.Delete(targetSessionPath); } catch { }
            try
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !File.Exists(targetSessionPath))
                    File.Move(backupPath, targetSessionPath, overwrite: true);
            }
            catch { }
            return false;
        }
    }

    public static bool LooksLikeSqliteSession(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[16];
            var read = fs.Read(header);
            if (read < 15) return false;
            var text = Encoding.ASCII.GetString(header[..15]);
            return string.Equals(text, "SQLite format 3", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryFindAnySessionJsonPath(string phone, string absoluteSessionPath)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        // 1) 优先找与 session 同目录的 phone.json（最常见：sessions/<phone>.json）
        var sessionDir = Path.GetDirectoryName(absoluteSessionPath);
        if (!string.IsNullOrWhiteSpace(sessionDir))
        {
            var direct = Path.Combine(sessionDir, $"{normalizedPhone}.json");
            if (File.Exists(direct))
                return direct;

            var anyInSessionDir = Directory.EnumerateFiles(sessionDir, "*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(p => string.Equals(NormalizePhone(Path.GetFileNameWithoutExtension(p)), normalizedPhone, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(anyInSessionDir))
                return anyInSessionDir;
        }

        // 2) 尝试在仓库根目录的 session数据/<phone> 下找
        var repoRoot = TryFindRepoRoot();
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var sessionDataDir = Path.Combine(repoRoot, "session数据", normalizedPhone);
            var direct = Path.Combine(sessionDataDir, $"{normalizedPhone}.json");
            if (File.Exists(direct))
                return direct;

            if (Directory.Exists(sessionDataDir))
            {
                var any = Directory.EnumerateFiles(sessionDataDir, "*.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(any))
                    return any;
            }

            // 3) 兜底：在 session数据 下递归扫描 phone 字段匹配
            var sessionDataRoot = Path.Combine(repoRoot, "session数据");
            var scanned = TryScanJsonByPhone(sessionDataRoot, normalizedPhone);
            if (!string.IsNullOrWhiteSpace(scanned))
                return scanned;

            // 4) 再兜底：在 sessions 下递归扫描（部分用户会把 json 放在 sessions/ 里）
            var sessionsRoot = Path.Combine(repoRoot, "sessions");
            scanned = TryScanJsonByPhone(sessionsRoot, normalizedPhone);
            if (!string.IsNullOrWhiteSpace(scanned))
                return scanned;
        }

        return null;
    }

    private static byte[] DecodeBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        var mod = s.Length % 4;
        if (mod == 2) s += "==";
        else if (mod == 3) s += "=";

        try
        {
            return Convert.FromBase64String(s);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private readonly record struct TelethonSessionData(int DcId, string IpAddress, ushort Port, byte[] AuthKey);

    private static bool TryParseTelethonStringSession(string sessionString, out TelethonSessionData data)
    {
        try
        {
            data = default;
            if (string.IsNullOrWhiteSpace(sessionString) || sessionString.Length < 16)
                return false;

            // Telethon StringSession: first char is version digit
            if (!char.IsDigit(sessionString[0]))
                return false;

            var body = sessionString.Substring(1);
            var packed = DecodeBase64Url(body);

            // 常见 Telethon packed bytes 长度：
            // - IPv4: 263 = 1(dc_id)+4(ip)+2(port)+256(auth_key)
            // - IPv6: 275 = 1+16+2+256
            if (packed.Length is not (263 or 275))
                return false;

            var dcId = packed[0];
            var ipLen = packed.Length - 1 - 2 - 256;
            if (ipLen is not (4 or 16))
                return false;

            var ipBytes = packed.AsSpan(1, ipLen).ToArray();
            var ip = new IPAddress(ipBytes).ToString();
            var port = BinaryPrimitives.ReadUInt16BigEndian(packed.AsSpan(1 + ipLen, 2));
            var authKey = packed.AsSpan(1 + ipLen + 2, 256).ToArray();

            data = new TelethonSessionData(dcId, ip, port, authKey);
            return true;
        }
        catch
        {
            data = default;
            return false;
        }
    }

    private static async Task<bool> WriteWTelegramSessionFileAsync(
        int apiId,
        string apiHash,
        string sessionPath,
        string phoneDigits,
        long? userId,
        int dcId,
        string ipAddress,
        ushort port,
        byte[] authKey,
        ILogger logger)
    {
        string Config(string what) => what switch
        {
            "api_id" => apiId.ToString(),
            "api_hash" => apiHash,
            "session_key" => apiHash,
            "session_pathname" => sessionPath,
            "phone_number" => phoneDigits,
            "user_id" => userId?.ToString() ?? "-1",
            _ => null!
        };

        // 1) 创建空 session 文件
        await using (var builder = new Client(Config))
        {
            var clientType = typeof(Client);
            var sessionField = clientType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法访问 WTelegram.Client._session");
            var dcSessionField = clientType.GetField("_dcSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法访问 WTelegram.Client._dcSession");

            var sessionObj = sessionField.GetValue(builder) ?? throw new InvalidOperationException("WTelegram session 未初始化");
            var sessionType = sessionObj.GetType();

            var dcSessionType = sessionType.GetNestedType("DCSession", BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法访问 WTelegram.Session.DCSession");
            var dcSessionObj = Activator.CreateInstance(dcSessionType) ?? throw new InvalidOperationException("无法创建 DCSession");

            // TL.DcOption 是公开类型，直接构造
            var dcOption = new DcOption { id = dcId, ip_address = ipAddress, port = port, flags = 0 };

            // 填充 DCSession：AuthKey + DataCenter + UserId + authKeyID
            dcSessionType.GetField("AuthKey")?.SetValue(dcSessionObj, authKey);
            dcSessionType.GetField("UserId")?.SetValue(dcSessionObj, userId ?? 0);
            dcSessionType.GetField("DataCenter")?.SetValue(dcSessionObj, dcOption);
            dcSessionType.GetField("Layer")?.SetValue(dcSessionObj, 0);

            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(authKey);
                var authKeyId = BinaryPrimitives.ReadInt64LittleEndian(hash.AsSpan(12, 8));
                dcSessionType.GetField("authKeyID", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(dcSessionObj, authKeyId);
            }

            dcSessionType.GetField("Client", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(dcSessionObj, builder);

            // 填充 Session：MainDC + UserId + DcOptions + DCSessions
            sessionType.GetField("MainDC")?.SetValue(sessionObj, dcId);
            sessionType.GetField("UserId")?.SetValue(sessionObj, userId ?? 0);
            sessionType.GetField("DcOptions")?.SetValue(sessionObj, new[] { dcOption });

            var dcSessionsField = sessionType.GetField("DCSessions");
            var dcSessions = dcSessionsField?.GetValue(sessionObj) as System.Collections.IDictionary;
            if (dcSessions == null)
                throw new InvalidOperationException("无法访问 Session.DCSessions");
            dcSessions[dcId] = dcSessionObj;

            // 同步 builder 当前 dcSession 引用
            dcSessionField.SetValue(builder, dcSessionObj);

            // 保存 session（写入加密 JSON）
            var save = sessionType.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法访问 Session.Save()");
            lock (sessionObj) save.Invoke(sessionObj, null);
        }

        // 2) 用 LoginUserIfNeeded 验证（不允许自动重新登录，避免进入要验证码流程）
        await using var probe = new Client(Config);
        try
        {
            _ = await probe.LoginUserIfNeeded(reloginOnFailedResume: false);
            logger.LogInformation("WTelegram session validated for {Phone} on DC {DcId}", phoneDigits, dcId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WTelegram session validation failed for {Phone}", phoneDigits);
            return false;
        }
    }

    private static string BuildBackupPath(string originalPath, string suffix)
    {
        var fullPath = Path.GetFullPath(originalPath);
        var dir = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        return Path.Combine(dir, $"{name}.{suffix}{ext}");
    }

    private static string? TryFindRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10 && !string.IsNullOrWhiteSpace(current); i++)
        {
            if (File.Exists(Path.Combine(current, "TelegramPanel.sln")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static string? TryScanJsonByPhone(string rootDir, string normalizedPhone)
    {
        try
        {
            if (!Directory.Exists(rootDir))
                return null;

            foreach (var jsonPath in Directory.EnumerateFiles(rootDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(jsonPath);
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("phone", out var phoneProp) && phoneProp.ValueKind == JsonValueKind.String)
                    {
                        var p = NormalizePhone(phoneProp.GetString());
                        if (string.Equals(p, normalizedPhone, StringComparison.Ordinal))
                            return jsonPath;
                    }
                }
                catch
                {
                    // ignore single json parse error
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new StringBuilder(phone.Length);
        foreach (var ch in phone)
        {
            if (ch >= '0' && ch <= '9')
                digits.Append(ch);
        }
        return digits.ToString();
    }

    // 备份逻辑在 TryCreateWTelegramSessionFromSessionStringAsync 内集中处理
}
