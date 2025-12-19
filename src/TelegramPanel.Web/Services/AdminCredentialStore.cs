using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TelegramPanel.Web.Services;

public sealed class AdminCredentialStore
{
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<AdminAuthOptions> _options;
    private readonly ILogger<AdminCredentialStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AdminCredentialFile? _cached;

    public AdminCredentialStore(
        IWebHostEnvironment environment,
        IOptionsMonitor<AdminAuthOptions> options,
        ILogger<AdminCredentialStore> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public bool Enabled => _options.CurrentValue.Enabled;

    public string Username => (_cached?.Username ?? _options.CurrentValue.InitialUsername).Trim();

    public bool MustChangePassword => _cached?.MustChangePassword == true;

    public string CredentialsFilePath => Path.Combine(_environment.ContentRootPath, _options.CurrentValue.CredentialsPath);

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached != null)
                return;

            var path = CredentialsFilePath;
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                _cached = JsonSerializer.Deserialize<AdminCredentialFile>(json) ?? throw new InvalidOperationException("admin_auth.json 解析失败");
                return;
            }

            var opt = _options.CurrentValue;
            var initialUsername = (opt.InitialUsername ?? "admin").Trim();
            var initialPassword = (opt.InitialPassword ?? "admin123").Trim();
            if (string.IsNullOrWhiteSpace(initialUsername) || string.IsNullOrWhiteSpace(initialPassword))
                throw new InvalidOperationException("AdminAuth 初始账号/密码未配置");

            var now = DateTime.UtcNow;
            var file = CreateCredentialFile(initialUsername, initialPassword, mustChangePassword: true, now);

            await SaveAsync(file, cancellationToken);
            _cached = file;

            _logger.LogWarning("后台登录已初始化：账号 {Username}，初始密码已设置（请首次登录后立即修改）", initialUsername);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ValidateAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return true;

        await EnsureInitializedAsync(cancellationToken);

        username = (username ?? string.Empty).Trim();
        password = (password ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = _cached ?? throw new InvalidOperationException("凭据未初始化");
            if (!string.Equals(username, file.Username, StringComparison.Ordinal))
                return false;

            return VerifyPassword(file, password);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            throw new InvalidOperationException("后台验证未启用");

        await EnsureInitializedAsync(cancellationToken);

        currentPassword = (currentPassword ?? string.Empty).Trim();
        newPassword = (newPassword ?? string.Empty).Trim();

        if (newPassword.Length < 6)
            throw new InvalidOperationException("新密码长度至少 6 位");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = _cached ?? throw new InvalidOperationException("凭据未初始化");
            if (!VerifyPassword(file, currentPassword))
                throw new InvalidOperationException("当前密码错误");

            var now = DateTime.UtcNow;
            var updated = CreateCredentialFile(file.Username, newPassword, mustChangePassword: false, now);
            updated.CreatedAtUtc = file.CreatedAtUtc;

            await SaveAsync(updated, cancellationToken);
            _cached = updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(AdminCredentialFile file, CancellationToken cancellationToken)
    {
        var path = CredentialsFilePath;
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static AdminCredentialFile CreateCredentialFile(string username, string password, bool mustChangePassword, DateTime nowUtc)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iterations = 150_000;
        var hash = HashPassword(password, salt, iterations);

        return new AdminCredentialFile
        {
            Version = 1,
            Username = username,
            SaltBase64 = Convert.ToBase64String(salt),
            HashBase64 = Convert.ToBase64String(hash),
            Iterations = iterations,
            MustChangePassword = mustChangePassword,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    private static bool VerifyPassword(AdminCredentialFile file, string password)
    {
        var salt = Convert.FromBase64String(file.SaltBase64);
        var expected = Convert.FromBase64String(file.HashBase64);
        var actual = HashPassword(password, salt, file.Iterations);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private sealed class AdminCredentialFile
    {
        public int Version { get; set; }
        public string Username { get; set; } = "admin";
        public string SaltBase64 { get; set; } = "";
        public string HashBase64 { get; set; } = "";
        public int Iterations { get; set; } = 150_000;
        public bool MustChangePassword { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}

