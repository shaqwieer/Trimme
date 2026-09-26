namespace Trimme.Modules.Identity.Application.Sessions;

/// <summary>
/// Turns a user agent into a coarse label ("Chrome · Windows") for the session list. The raw user agent is not stored,
/// since it adds fingerprinting detail without helping the user recognise a device.
/// </summary>
internal static class DeviceLabels
{
    public const string Unknown = "Unknown device";

    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unknown;
        }

        var browser = Browser(userAgent);
        var os = OperatingSystem(userAgent);
        return (browser, os) switch
        {
            (null, null) => Unknown,
            (null, _) => os!,
            (_, null) => browser,
            _ => $"{browser} · {os}",
        };
    }

    private static string? Browser(string ua) =>
        ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
        : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
        : ua.Contains("SamsungBrowser/", StringComparison.Ordinal) ? "Samsung Internet"
        : ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS/", StringComparison.Ordinal) ? "Firefox"
        : ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) ? "Chrome"
        : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
        : null;

    private static string? OperatingSystem(string ua) =>
        ua.Contains("Android", StringComparison.Ordinal) ? "Android"
        : ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) ? "iOS"
        : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
        : ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal) ? "macOS"
        : ua.Contains("CrOS", StringComparison.Ordinal) ? "ChromeOS"
        : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
        : null;
}
