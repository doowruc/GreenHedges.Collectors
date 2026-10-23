namespace GreenHedges.HomeServer.Common.Alerts;

/// <summary>
/// Alert delivery settings shared by every collector (section <c>Alerts</c>). A collector binds its own
/// thresholds from the same section.
/// </summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Resend API key. If empty, alerts are only logged.</summary>
    public string? ResendApiKey { get; set; }

    /// <summary>Sender, e.g. <c>LibreCollector &lt;alerts@example.com&gt;</c>. Must be on a domain verified in Resend.</summary>
    public string? From { get; set; }

    public string[] To { get; set; } = [];

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// File that keeps open alerts across restarts. A relative path is resolved against the working
    /// directory, like the database.
    /// </summary>
    public string StateFile { get; set; } = "alert-state.json";

    public bool EmailEnabled => !string.IsNullOrWhiteSpace(ResendApiKey);
}
