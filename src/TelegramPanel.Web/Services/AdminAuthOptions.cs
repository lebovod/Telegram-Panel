namespace TelegramPanel.Web.Services;

public sealed class AdminAuthOptions
{
    public bool Enabled { get; set; }
    public string InitialUsername { get; set; } = "admin";
    public string InitialPassword { get; set; } = "admin123";
    public string CredentialsPath { get; set; } = "admin_auth.json";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(InitialUsername) && !string.IsNullOrWhiteSpace(InitialPassword);
}
