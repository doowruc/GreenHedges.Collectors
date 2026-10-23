using Microsoft.Extensions.Logging;

namespace GreenHedges.HomeServer.Collectors.Weather.Rtl433;

/// <summary>
/// Keeps rtl_433 running: whenever a run ends (exit, silence, failure to start, missing dongle) it starts
/// another after a back-off, so a missing dongle or a crash loop slows down instead of spinning. Problems
/// show in <see cref="ReceiverHealth"/> for the alerts; they never stop the collector.
/// </summary>
public sealed class Rtl433Supervisor(Rtl433Runner runner, Rtl433Options options, TimeProvider time, ILogger<Rtl433Supervisor> logger)
{
    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(Func<string, Task> onLine, CancellationToken stoppingToken)
    {
        var endsInARow = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await runner.RunOnceAsync(onLine, stoppingToken);
            if (result.End == RunEnd.Stopped)
            {
                break;
            }

            endsInARow = result.Duration >= options.StableAfter ? 1 : endsInARow + 1;
            var delay = RestartDelay(endsInARow, options);
            logger.LogWarning(
                "Restarting rtl_433 in {Delay}{Hint}",
                delay,
                result.DongleMissing ? " (no usable RTL-SDR dongle: is it plugged in?)" : "");

            try
            {
                await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>The wait before restart number <paramref name="endsInARow"/> (1-based): doubling from the minimum, capped.</summary>
    internal static TimeSpan RestartDelay(int endsInARow, Rtl433Options options)
    {
        var doublings = Math.Clamp(endsInARow - 1, 0, 30);
        var delay = options.MinRestartDelay * Math.Pow(2, doublings);
        return delay < options.MaxRestartDelay ? delay : options.MaxRestartDelay;
    }
}
