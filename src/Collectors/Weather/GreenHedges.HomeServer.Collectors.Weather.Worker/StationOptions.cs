namespace GreenHedges.HomeServer.Collectors.Weather.Worker;

/// <summary>Which device on the air is "my station".</summary>
public sealed class StationOptions
{
    public const string SectionName = "Station";

    /// <summary>The station's rtl_433 model.</summary>
    public string Model { get; set; } = "Bresser-6in1";

    /// <summary>
    /// The station's device ids, as rtl_433 writes them. A list: after a battery change the sensor may pick
    /// a new id, which is added here while the old one stays, so the old readings stay the station's.
    /// Keep them out of the repository (env file: <c>Station__DeviceIds__0=…</c>).
    /// </summary>
    public string[] DeviceIds { get; set; } = [];

    public bool IsStation(string? model, string? deviceId) =>
        model == Model && deviceId is not null && DeviceIds.Contains(deviceId);
}

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    /// <summary>The SQLite archive. A relative path is resolved against the working directory.</summary>
    public string DatabasePath { get; set; } = "weathercollector.db";
}
