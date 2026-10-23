using GreenHedges.HomeServer.Common.Heartbeat;

namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>
/// What the app has done lately, for the alert checks (<see cref="VehicleManagerAlertMonitor"/>) and the heartbeat: the
/// database probes and the scheduled jobs' runs. In memory, so it starts empty after a restart.
/// </summary>
public sealed class AppHealth(TimeProvider time) : IHeartbeatSource
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ScheduledJob, JobRun> _lastRuns = [];
    private readonly Dictionary<ScheduledJob, DateTimeOffset> _lastSuccesses = [];
    private DateTimeOffset? _databaseOkAt;
    private DateTimeOffset? _databaseFailingSince;
    private string? _databaseError;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    public void RecordDatabaseOk()
    {
        lock (_lock)
        {
            _databaseOkAt = time.GetUtcNow();
            _databaseFailingSince = null;
            _databaseError = null;
        }
    }

    public void RecordDatabaseFailure(string error)
    {
        lock (_lock)
        {
            _databaseFailingSince ??= time.GetUtcNow();
            _databaseError = error;
        }
    }

    /// <summary>Records a finished run of <paramref name="job"/> (including one that failed as a whole).</summary>
    public void RecordRun(ScheduledJob job, int users, int failures, Exception? lastError)
    {
        var run = new JobRun(time.GetUtcNow(), users, failures, lastError is null ? null : $"{lastError.GetType().Name}: {lastError.Message}");
        lock (_lock)
        {
            _lastRuns[job] = run;
            if (run.Succeeded) _lastSuccesses[job] = run.CompletedAt;
        }
    }

    /// <summary>Healthy for the heartbeat while the database answered within the interval.</summary>
    public string? HealthyStatus(DateTimeOffset now, TimeSpan interval)
    {
        var snapshot = Snapshot();
        return snapshot.DatabaseOkAt is { } ok && now - ok <= interval ? $"Database answered {ok:u}" : null;
    }

    public AppHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new AppHealthSnapshot(
                StartedAt, _databaseOkAt, _databaseFailingSince, _databaseError,
                new Dictionary<ScheduledJob, JobRun>(_lastRuns), new Dictionary<ScheduledJob, DateTimeOffset>(_lastSuccesses));
        }
    }
}
