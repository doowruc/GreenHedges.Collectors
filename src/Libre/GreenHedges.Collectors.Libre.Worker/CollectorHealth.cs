namespace GreenHedges.Collectors.Libre.Worker;

/// <summary>
/// What the collector has done lately, shared with the alert monitor. Kept separate from the
/// collector loop so a stuck or paused loop still shows up as stale data.
/// </summary>
public sealed class CollectorHealth(TimeProvider time)
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _latestReadingAt;
    private DateTimeOffset? _lastSuccessAt;
    private Exception? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private DateTimeOffset? _lastBackupAt;
    private Exception? _lastBackupError;
    private DateTimeOffset? _publishFailingSince;
    private DateTimeOffset? _lastPublishSuccessAt;
    private DateTimeOffset? _lastOffsiteAt;
    private Exception? _lastOffsiteError;
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

    /// <summary>Records a completed backup. <paramref name="completedAt"/> may be in the past (an existing backup found at startup).</summary>
    public void RecordBackup(DateTimeOffset completedAt)
    {
        lock (_lock)
        {
            if (_lastBackupAt is null || completedAt > _lastBackupAt)
            {
                _lastBackupAt = completedAt;
            }

            _lastBackupError = null;
        }
    }

    public void RecordBackupFailure(Exception error)
    {
        lock (_lock)
        {
            _lastBackupError = error;
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

    /// <summary>Records a successful (or already up-to-date) off-site copy. Keeps the newest time.</summary>
    public void RecordOffsite(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_lastOffsiteAt is null || at > _lastOffsiteAt)
            {
                _lastOffsiteAt = at;
            }

            _lastOffsiteError = null;
        }
    }

    public void RecordOffsiteFailure(Exception error)
    {
        lock (_lock)
        {
            _lastOffsiteError = error;
        }
    }

    public CollectorHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new CollectorHealthSnapshot(
                StartedAt, _latestReadingAt, _lastSuccessAt, _lastError, _lastErrorAt, _lastBackupAt, _lastBackupError,
                _publishFailingSince, _lastPublishError, _lastPublishSuccessAt, _lastOffsiteAt, _lastOffsiteError);
        }
    }
}

/// <param name="LatestReadingAt">Sensor time of the newest current reading seen since startup.</param>
/// <param name="LastError">The most recent failure, cleared by the next successful poll.</param>
/// <param name="LastBackupAt">When the newest good backup was made (including one found on disk at startup).</param>
/// <param name="LastBackupError">The most recent backup failure, cleared by the next successful backup.</param>
/// <param name="PublishFailingSince">When Nightscout publishing started failing; null while it's healthy.</param>
public sealed record CollectorHealthSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? LatestReadingAt,
    DateTimeOffset? LastSuccessAt,
    Exception? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset? LastBackupAt = null,
    Exception? LastBackupError = null,
    DateTimeOffset? PublishFailingSince = null,
    Exception? LastPublishError = null,
    DateTimeOffset? LastPublishSuccessAt = null,
    DateTimeOffset? LastOffsiteAt = null,
    Exception? LastOffsiteError = null);
