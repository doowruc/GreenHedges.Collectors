namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <param name="DatabaseOkAt">When the database last answered a probe, since startup.</param>
/// <param name="DatabaseFailingSince">When probes started failing; null while the database answers.</param>
/// <param name="LastRuns">Each job's latest run since startup.</param>
/// <param name="LastSuccesses">When each job last ran without a failure, since startup.</param>
public sealed record AppHealthSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? DatabaseOkAt,
    DateTimeOffset? DatabaseFailingSince,
    string? DatabaseError,
    IReadOnlyDictionary<ScheduledJob, JobRun> LastRuns,
    IReadOnlyDictionary<ScheduledJob, DateTimeOffset> LastSuccesses);
