using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker;

/// <summary>
/// What the collector has archived lately, for the alert checks and the heartbeat. The receiver's own
/// state (rtl_433 running, restarts, dongle) is in <see cref="ReceiverHealth"/>.
/// </summary>
public sealed class CollectorHealth(TimeProvider time) : IHeartbeatSource
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _lastStationMessageAt;
    private string? _lastStationDeviceId;
    private int? _lastBatteryOk;
    private DateTimeOffset? _batteryLowSince;
    private DateTimeOffset? _lastArchivedAt;
    private Exception? _lastArchiveError;
    private DateTimeOffset? _lastArchiveErrorAt;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    public void RecordArchived(DateTimeOffset at)
    {
        lock (_lock)
        {
            _lastArchivedAt = at;
            _lastArchiveError = null;
        }
    }

    public void RecordStationMessage(DateTimeOffset at, string deviceId, WeatherFields weather)
    {
        lock (_lock)
        {
            _lastStationMessageAt = at;
            _lastStationDeviceId = deviceId;
            // Only temperature messages carry the battery flag.
            if (weather.BatteryOk is { } batteryOk)
            {
                _lastBatteryOk = batteryOk;
                _batteryLowSince = batteryOk == 0 ? _batteryLowSince ?? at : null;
            }
        }
    }

    public void RecordArchiveFailure(Exception error)
    {
        lock (_lock)
        {
            _lastArchiveError = error;
            _lastArchiveErrorAt = time.GetUtcNow();
        }
    }

    /// <summary>Healthy for the heartbeat if a station message arrived within the interval.</summary>
    public string? HealthyStatus(DateTimeOffset now, TimeSpan interval)
    {
        var snapshot = Snapshot();
        return snapshot.LastStationMessageAt is { } last && now - last <= interval
            ? $"Last station message {last:u}"
            : null;
    }

    public CollectorHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new CollectorHealthSnapshot(
                StartedAt, _lastStationMessageAt, _lastStationDeviceId, _lastBatteryOk, _batteryLowSince,
                _lastArchivedAt, _lastArchiveError, _lastArchiveErrorAt);
        }
    }
}

/// <param name="LastBatteryOk">The station's latest <c>battery_ok</c> (1 OK, 0 low), if seen since startup.</param>
/// <param name="BatteryLowSince">When the station's <c>battery_ok</c> turned 0, while it stays 0; null once it's 1 (or not seen).</param>
/// <param name="LastArchiveError">The latest failure to archive a line, cleared by the next success.</param>
public sealed record CollectorHealthSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? LastStationMessageAt,
    string? LastStationDeviceId,
    int? LastBatteryOk,
    DateTimeOffset? BatteryLowSince,
    DateTimeOffset? LastArchivedAt,
    Exception? LastArchiveError,
    DateTimeOffset? LastArchiveErrorAt);
