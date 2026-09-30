namespace Trimme.Modules.QrAnalytics.Application;

/// <summary>QR attribution settings (D-114). Bound from <c>QrAnalytics</c>.</summary>
public sealed class QrOptions
{
    public const string SectionName = "QrAnalytics";

    /// <summary>How long after a scan a booking of the same shop is credited to the code (R-QR-02; default 7 days).</summary>
    public int AttributionDays { get; set; } = 7;

    /// <summary>A reload of the landing page within this many minutes reuses the visit instead of counting a new scan.</summary>
    public int ReloadWindowMinutes { get; set; } = 30;
}

/// <summary>Where the web app lives: the printed codes encode <c>{PublicBaseUrl}/q/{code}</c>. Bound from <c>Web</c>.</summary>
public sealed class QrLinkOptions
{
    public const string SectionName = "Web";

    public string PublicBaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>The URL printed in the code: no locale, so the web app picks the visitor's language (<c>/q/{code}</c> → <c>/ar/q/{code}</c>).</summary>
    public string UrlFor(string code) => $"{PublicBaseUrl.TrimEnd('/')}/q/{code}";
}
