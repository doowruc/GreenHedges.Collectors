using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Common.Heartbeat;

public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    /// <summary>
    /// Ping URL of an external monitor, e.g. <c>https://hc-ping.com/&lt;uuid&gt;</c> (healthchecks.io).
    /// If empty, no heartbeat is sent. Treat it as a secret: anyone with it can send pings.
    /// </summary>
    public string? PingUrl { get; set; }

    /// <summary>How often to ping. Set the monitor's period to match, plus a grace time.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>What the heartbeat asks the collector before each ping.</summary>
public interface IHeartbeatSource
{
    /// <summary>
    /// A short status for the ping if the collector has worked within the last <paramref name="interval"/>,
    /// or null to skip the ping.
    /// </summary>
    string? HealthyStatus(DateTimeOffset now, TimeSpan interval);
}

/// <summary>
/// Off-machine dead man's switch. Pings an external monitor while the collector is working. If this
/// machine is off, stuck at the LUKS prompt, offline, or the collector keeps failing, the pings stop
/// and the external service sends the alert, which the on-machine alert monitor can't do in those cases.
/// </summary>
public sealed class HeartbeatService(
    HttpClient http,
    IHeartbeatSource source,
    IOptions<HeartbeatOptions> options,
    TimeProvider time,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    private readonly HeartbeatOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PingUrl))
        {
            logger.LogWarning("Heartbeat:PingUrl is not set; no off-machine heartbeat will be sent");
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval, time);
        try
        {
            do
            {
                await PingIfHealthyAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Pings if the collector has worked within the last interval. Never throws.</summary>
    public async Task<bool> PingIfHealthyAsync(CancellationToken cancellationToken)
    {
        if (source.HealthyStatus(time.GetUtcNow(), _options.Interval) is not { } status)
        {
            logger.LogDebug("Heartbeat skipped: not healthy in the last {Interval}", _options.Interval);
            return false;
        }

        try
        {
            using var content = new StringContent(status);
            using var response = await http.PostAsync(_options.PingUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Heartbeat ping returned HTTP {StatusCode}", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Heartbeat ping failed");
            return false;
        }
    }
}
