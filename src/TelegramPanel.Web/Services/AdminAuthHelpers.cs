namespace TelegramPanel.Web.Services;

public static class AdminAuthHelpers
{
    public static bool IsLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return false;

        // 仅允许站内跳转，避免 open redirect
        if (!returnUrl.StartsWith("/", StringComparison.Ordinal))
            return false;

        if (returnUrl.StartsWith("//", StringComparison.Ordinal))
            return false;

        return true;
    }
}

