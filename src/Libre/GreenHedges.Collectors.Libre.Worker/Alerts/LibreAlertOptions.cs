namespace GreenHedges.Collectors.Libre.Worker.Alerts;

/// <summary>The Libre collector's alert thresholds, bound from the shared <c>Alerts</c> section.</summary>
public sealed class LibreAlertOptions
{
    /// <summary>Alert if the newest current reading is older than this.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Alert if Nightscout publishing has been failing for this long.</summary>
    public TimeSpan PublishStalledAfter { get; set; } = TimeSpan.FromMinutes(30);
}
