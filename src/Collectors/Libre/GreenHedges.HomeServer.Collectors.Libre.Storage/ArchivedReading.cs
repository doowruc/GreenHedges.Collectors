namespace GreenHedges.HomeServer.Collectors.Libre.Storage;

public enum ReadingSource
{
    /// <summary>Per-minute reading from <c>/llu/connections</c> (or the graph's current measurement).</summary>
    Current,

    /// <summary>Smoothed 15-minute history from <c>/llu/connections/{id}/graph</c>.</summary>
    Graph,

    /// <summary>Historic (15-minute) glucose imported from a LibreView CSV export.</summary>
    LibreView,

    /// <summary>Scan glucose (one per LibreLink app open) imported from a LibreView CSV export.</summary>
    LibreViewScan,
}

/// <param name="TimestampUtc">Sensor time (LibreLinkUp <c>FactoryTimestamp</c>). Part of the key.</param>
/// <param name="TimestampLocal">LibreLinkUp <c>Timestamp</c> exactly as received.</param>
/// <param name="ReceivedAtUtc">When the collector fetched it.</param>
/// <param name="RawJson">The original measurement object.</param>
/// <param name="SensorSerial">Serial of the sensor in use when a <c>current</c> reading was collected (schema v3+); null for graph history and older rows.</param>
public sealed record ArchivedReading(
    DateTimeOffset TimestampUtc,
    ReadingSource Source,
    string? TimestampLocal,
    double ValueMgPerDl,
    int? TrendArrow,
    int? MeasurementColor,
    bool IsHigh,
    bool IsLow,
    DateTimeOffset ReceivedAtUtc,
    string RawJson,
    string? SensorSerial = null);

/// <summary>An archived reading with its stable id.</summary>
/// <param name="HasNearbyBetter">
/// A reading from a higher-priority source (current, then graph, then libreview, then libreview_scan) exists
/// close to this reading's timestamp (see <see cref="GlucoseArchive.ReadAfterAsync"/>).
/// </param>
public sealed record StoredReading(long Id, ArchivedReading Reading, bool HasNearbyBetter);

/// <summary>A non-glucose record (insulin, carbs, notes, …), e.g. from a LibreView export.</summary>
/// <param name="Kind">e.g. <c>long_acting_insulin</c>, <c>rapid_acting_insulin</c>, <c>carbs</c>, <c>food</c>, <c>note</c>.</param>
/// <param name="Value">Numeric value, if the record has one.</param>
/// <param name="Unit">Unit of <paramref name="Value"/>, e.g. <c>units</c>, <c>g</c>, <c>mmol/L</c>.</param>
/// <param name="Text">Free text, for records without a numeric value.</param>
/// <param name="Source">Where it came from, e.g. <c>libreview</c>.</param>
public sealed record ArchivedEvent(
    DateTimeOffset TimestampUtc,
    string? TimestampLocal,
    string Kind,
    double? Value,
    string? Unit,
    string? Text,
    string Source,
    DateTimeOffset ReceivedAtUtc,
    string RawJson);

/// <summary>A sensor seen on the connection, as archived in the <c>sensors</c> table.</summary>
/// <param name="ActivatedAtUtc">Activation time from LibreLinkUp (<c>sensor.a</c>).</param>
/// <param name="FirstSeenUtc">When the collector first saw this sensor.</param>
/// <param name="LastSeenUtc">When the collector last saw this sensor.</param>
/// <param name="RawJson">The latest raw <c>sensor</c> object.</param>
public sealed record ArchivedSensor(
    string Serial,
    DateTimeOffset? ActivatedAtUtc,
    int? WarmupMinutes,
    int? ProductType,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    string RawJson);
