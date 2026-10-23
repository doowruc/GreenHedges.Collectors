using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker;

public enum SensorObservation
{
    /// <summary>The connection reported no sensor.</summary>
    None,

    /// <summary>The first sensor this archive has seen.</summary>
    FirstSeen,

    Unchanged,

    /// <summary>A different sensor from the last one seen (a sensor swap).</summary>
    Changed,
}

/// <summary>
/// Records the connection's sensor in the archive on each poll and logs when it changes.
/// Step 1 of sensor change detection (see docs/DESIGN.md backlog): observe and record only; no alert changes.
/// </summary>
public sealed class SensorTracker(
    GlucoseArchive archive,
    IOptions<CollectorOptions> options,
    TimeProvider time,
    ILogger<SensorTracker> logger)
{
    private readonly CollectorOptions _options = options.Value;
    private string? _currentSerial;
    private bool _loaded;

    /// <summary>Logs the most recently seen sensor and its expected end. For startup.</summary>
    public async Task LogCurrentAsync(CancellationToken cancellationToken)
    {
        var latest = await archive.GetLatestSensorAsync(cancellationToken);
        _currentSerial = latest?.Serial;
        _loaded = true;

        if (latest is null)
        {
            logger.LogInformation("No sensor recorded yet");
            return;
        }

        if (latest.ActivatedAtUtc is { } activated)
        {
            var end = activated + _options.SensorLifetime;
            logger.LogInformation(
                "Current sensor {Serial}: activated {Activated:u}, expected end {End:u} ({Remaining} left)",
                latest.Serial,
                activated,
                end,
                FormatRemaining(end - time.GetUtcNow()));
        }
        else
        {
            logger.LogInformation("Current sensor {Serial}: activation time unknown", latest.Serial);
        }
    }

    public async Task<SensorObservation> ObserveAsync(SensorInfo? sensor, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _currentSerial = (await archive.GetLatestSensorAsync(cancellationToken))?.Serial;
            _loaded = true;
        }

        if (sensor is null)
        {
            return SensorObservation.None;
        }

        await archive.RecordSensorAsync(
            sensor.Serial,
            sensor.ActivatedAt,
            sensor.WarmupMinutes,
            sensor.ProductType,
            sensor.RawJson,
            time.GetUtcNow(),
            cancellationToken);

        if (sensor.Serial == _currentSerial)
        {
            return SensorObservation.Unchanged;
        }

        var previous = _currentSerial;
        _currentSerial = sensor.Serial;
        var expectedEnd = sensor.ActivatedAt + _options.SensorLifetime;

        if (previous is null)
        {
            logger.LogInformation(
                "Sensor {Serial} in use: activated {Activated:u}, expected end {End:u}. Raw: {Raw}",
                sensor.Serial,
                sensor.ActivatedAt,
                expectedEnd,
                sensor.RawJson);
            return SensorObservation.FirstSeen;
        }

        logger.LogInformation(
            "Sensor changed: {Previous} -> {Serial}, activated {Activated:u}, warm-up {Warmup} min (readings expected from {ReadyAt:u}), expected end {End:u}. Raw: {Raw}",
            previous,
            sensor.Serial,
            sensor.ActivatedAt,
            sensor.WarmupMinutes,
            sensor.ActivatedAt + TimeSpan.FromMinutes(sensor.WarmupMinutes ?? 0),
            expectedEnd,
            sensor.RawJson);
        return SensorObservation.Changed;
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero
            ? "already past"
            : $"{(int)remaining.TotalDays}d {remaining.Hours}h";
}
