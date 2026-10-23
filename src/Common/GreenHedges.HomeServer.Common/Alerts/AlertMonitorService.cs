using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Common.Alerts;

/// <summary>
/// Runs a collector's health checks every <see cref="AlertOptions.CheckInterval"/>, raising and
/// resolving alerts in the <see cref="AlertBook"/>.
/// </summary>
/// <remarks>
/// Health state starts empty after a restart while open alerts are restored, so a check should only
/// resolve an alert on positive evidence gathered since startup, never because "no error has been
/// seen yet".
/// </remarks>
public abstract class AlertMonitorService(
    AlertBook alerts,
    IOptions<AlertOptions> options,
    TimeProvider time,
    ILogger logger) : BackgroundService
{
    private readonly AlertOptions _options = options.Value;

    protected AlertBook Alerts { get; } = alerts;

    protected TimeProvider Time { get; } = time;

    protected ILogger Logger { get; } = logger;

    /// <summary>Every key this collector raises. Saved alerts with other keys are dropped at startup.</summary>
    protected abstract IReadOnlyCollection<string> AlertKeys { get; }

    /// <summary>Restores alerts that were open when the collector last stopped.</summary>
    public Task LoadStateAsync(CancellationToken cancellationToken) => Alerts.LoadAsync(AlertKeys, cancellationToken);

    /// <summary>One round of checks.</summary>
    public abstract Task CheckAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EmailEnabled)
        {
            Logger.LogWarning("Alerts:ResendApiKey is not set; alerts will only be logged");
        }

        await LoadStateAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.CheckInterval, Time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogError(ex, "Alert check failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
