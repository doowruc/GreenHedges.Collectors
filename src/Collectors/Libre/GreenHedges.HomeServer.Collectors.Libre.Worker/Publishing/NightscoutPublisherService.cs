using System.Threading.Channels;
using GreenHedges.HomeServer.Collectors.Libre.Nightscout;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Publishing;

/// <summary>Wakes the publisher as soon as the collector stores new rows. Coalesces: many notifies, one wake.</summary>
public sealed class PublishSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Waits for a notify or the timeout, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await _channel.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out: publish anyway.
        }
    }
}

/// <summary>
/// Runs <see cref="NightscoutPublisher"/> whenever the collector stores new rows, and at least every
/// <see cref="NightscoutOptions.FallbackInterval"/>. If Nightscout is down it backs off, and collection is unaffected.
/// </summary>
public sealed class NightscoutPublisherService(
    IServiceProvider services,
    PublishSignal signal,
    CollectorHealth health,
    IOptions<NightscoutOptions> options,
    TimeProvider time,
    ILogger<NightscoutPublisherService> logger) : BackgroundService
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly NightscoutOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Nightscout publishing is off (Nightscout:BaseUrl not set)");
            return;
        }

        // Resolved only when enabled: the client needs BaseUrl.
        var publisher = services.GetRequiredService<NightscoutPublisher>();
        logger.LogInformation("Publishing to Nightscout at {BaseUrl}", _options.BaseUrl);

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var uploaded = await publisher.PublishPendingAsync(stoppingToken);
                health.RecordPublishSuccess();
                if (failures > 0 || uploaded >= 10)
                {
                    logger.LogInformation("Published {Uploaded} entries to Nightscout", uploaded);
                }

                failures = 0;
                await signal.WaitAsync(_options.FallbackInterval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                health.RecordPublishFailure(ex);
                var backoff = TimeSpan.FromTicks(Math.Min(
                    MinBackoff.Ticks * (1L << Math.Min(failures - 1, 10)),
                    MaxBackoff.Ticks));
                logger.LogWarning(ex, "Publishing to Nightscout failed ({Failures} in a row); retrying in {Backoff}", failures, backoff);
                try
                {
                    // Ignore collector signals while backing off.
                    await Task.Delay(backoff, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
