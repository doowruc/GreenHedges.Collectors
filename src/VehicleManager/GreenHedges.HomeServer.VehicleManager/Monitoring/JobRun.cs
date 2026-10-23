namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>One completed run of a scheduled job.</summary>
/// <param name="Users">How many users it ran for.</param>
/// <param name="Failures">How many of them failed, or 1 when the whole run failed before reaching any.</param>
/// <param name="LastError">The last failure's type and message, if any.</param>
public sealed record JobRun(DateTimeOffset CompletedAt, int Users, int Failures, string? LastError)
{
    public bool Succeeded => Failures == 0;
}
