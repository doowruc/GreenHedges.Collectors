namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>Vehicle Manager's alert thresholds, bound from the shared <c>Alerts</c> section.</summary>
public sealed class VehicleManagerAlertOptions
{
    /// <summary>Alert if the database hasn't answered for this long (probed every <c>Alerts:CheckInterval</c>).</summary>
    public TimeSpan DatabaseDownAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Alert if a daily job hasn't completed for this long (a day, plus a margin).</summary>
    public TimeSpan JobOverdueAfter { get; set; } = TimeSpan.FromHours(26);
}
