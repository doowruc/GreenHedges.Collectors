using GreenHedges.Collectors.Common.Heartbeat;

namespace GreenHedges.Collectors.Libre.Worker;

/// <summary>
/// What the collector has done lately, shared with the alert monitor. Kept separate from the
/// collector loop so a stuck or paused loop still shows up as stale data.
/// </summary>
public sealed class CollectorHealth(TimeProvider time) : IHeartbeatSource
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _latestReadingAt;
    private DateTimeOffset? _lastSuccessAt;
    private Exception? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private DateTimeOffset? _publishFailingSince;
    private DateTimeOffset? _lastPublishSuccessAt;
    private Exception? _lastPublishError;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    /// <summary>Records the sensor timestamp of a current reading. Keeps the newest seen.</summary>
    public void RecordReading(DateTimeOffset sensorTimestamp)
    {
        lock (_lock)
        {
            if (_latestReadingAt is null || sensorTimestamp > _latestReadingAt)
            {
                _latestReadingAt = sensorTimestamp;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _lastSuccessAt = time.GetUtcNow();
            _lastError = null;
            _lastErrorAt = null;
        }
    }

    public void RecordFailure(Exception error)
    {
        lock (_lock)
        {
            _lastError = error;
            _lastErrorAt = time.GetUtcNow();
        }
    }

    /// <summary>Records that everything pending was published.</summary>
    public void RecordPublishSuccess()
    {
        lock (_lock)
        {
            _publishFailingSince = null;
            _lastPublishError = null;
            _lastPublishSuccessAt = time.GetUtcNow();
        }
    }

    public void RecordPublishFailure(Exception error)
    {
        lock (_lock)
        {
            _publishFailingSince ??= time.GetUtcNow();
            _lastPublishError = error;
        }
    }

    /// <summary>Healthy for the heartbeat if LibreLinkUp was polled successfully within the interval.</summary>
    public string? HealthyStatus(DateTimeOffset now, TimeSpan interval)
    {
        var snapshot = Snapshot();
        return snapshot.LastSuccessAt is { } lastSuccess && now - lastSuccess <= interval
            ? $"Last successful poll {lastSuccess:u}; newest reading {snapshot.LatestReadingAt:u}"
            : null;
    }

    public CollectorHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new CollectorHealthSnapshot(
                StartedAt, _latestReadingAt, _lastSuccessAt, _lastError, _lastErrorAt,
                _publishFailingSince, _lastPublishError, _lastPublishSuccessAt);
        }
    }
}

/// <param name="LatestReadingAt">Sensor time of the newest current reading seen since startup.</param>
/// <param name="LastError">The most recent failure, cleared by the next successful poll.</param>
/// <param name="PublishFailingSince">When Nightscout publishing started failing; null while it's healthy.</param>
public sealed record CollectorHealthSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? LatestReadingAt,
    DateTimeOffset? LastSuccessAt,
    Exception? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset? PublishFailingSince = null,
    Exception? LastPublishError = null,
    DateTimeOffset? LastPublishSuccessAt = null);
