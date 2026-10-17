namespace GreenHedges.Collectors.Libre.Worker.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Resend API key. If empty, alerts are only logged.</summary>
    public string? ResendApiKey { get; set; }

    /// <summary>Sender, e.g. <c>LibreCollector &lt;alerts@example.com&gt;</c>. Must be on a domain verified in Resend.</summary>
    public string? From { get; set; }

    public string[] To { get; set; } = [];

    /// <summary>Alert if the newest current reading is older than this.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(20);

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Alert if Nightscout publishing has been failing for this long.</summary>
    public TimeSpan PublishStalledAfter { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// File that keeps open alerts across restarts. A relative path is resolved against the working
    /// directory, like the database.
    /// </summary>
    public string StateFile { get; set; } = "alert-state.json";

    public bool EmailEnabled => !string.IsNullOrWhiteSpace(ResendApiKey);
}
