namespace GreenHedges.Collectors.Common.Backups;

/// <summary>What backups and off-site copies have done lately, shared with the alert checks in <see cref="BackupMonitor"/>.</summary>
public sealed class BackupHealth(TimeProvider time)
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _lastBackupAt;
    private Exception? _lastBackupError;
    private DateTimeOffset? _lastOffsiteAt;
    private Exception? _lastOffsiteError;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

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

    public BackupHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new BackupHealthSnapshot(StartedAt, _lastBackupAt, _lastBackupError, _lastOffsiteAt, _lastOffsiteError);
        }
    }
}

/// <param name="LastBackupAt">When the newest good backup was made (including one found on disk at startup).</param>
/// <param name="LastBackupError">The most recent backup failure, cleared by the next successful backup.</param>
public sealed record BackupHealthSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? LastBackupAt,
    Exception? LastBackupError,
    DateTimeOffset? LastOffsiteAt,
    Exception? LastOffsiteError);
