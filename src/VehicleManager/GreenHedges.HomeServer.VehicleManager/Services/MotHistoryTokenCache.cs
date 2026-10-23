namespace VehicleManager.Services;

/// <summary>Holds the DVSA access token between requests (they last an hour), so each lookup doesn't fetch a new one.</summary>
public sealed class MotHistoryTokenCache
{
    internal readonly SemaphoreSlim Lock = new(1, 1);
    internal string? Token;
    internal DateTimeOffset Expires;
}
