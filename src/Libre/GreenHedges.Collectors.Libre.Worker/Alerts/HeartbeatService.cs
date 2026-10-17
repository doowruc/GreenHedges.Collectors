using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Libre.Worker.Alerts;

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

/// <summary>
/// Off-machine dead man's switch. Pings an external monitor while the collector is polling
/// successfully. If this machine is off, stuck at the LUKS prompt, offline, or the collector
/// keeps failing, the pings stop and the external service sends the alert, which the on-machine
/// <see cref="AlertMonitorService"/> can't do in those cases.
/// </summary>
public sealed class HeartbeatService(
    HttpClient http,
    CollectorHealth health,
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

    /// <summary>Pings if the collector has polled successfully within the last interval. Never throws.</summary>
    internal async Task<bool> PingIfHealthyAsync(CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();
        var now = time.GetUtcNow();

        if (snapshot.LastSuccessAt is not { } lastSuccess || now - lastSuccess > _options.Interval)
        {
            logger.LogDebug("Heartbeat skipped: no successful poll in the last {Interval}", _options.Interval);
            return false;
        }

        try
        {
            using var content = new StringContent(
                $"Last successful poll {lastSuccess:u}; newest reading {snapshot.LatestReadingAt:u}");
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
