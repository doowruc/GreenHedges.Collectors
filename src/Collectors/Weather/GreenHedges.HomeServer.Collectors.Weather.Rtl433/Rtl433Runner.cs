using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GreenHedges.HomeServer.Collectors.Weather.Rtl433;

/// <summary>
/// Runs rtl_433 once as a child process and hands each stdout line to the caller, until it exits, goes
/// silent for <see cref="Rtl433Options.SilenceTimeout"/> (then it's killed), or the collector stops.
/// stderr goes to the log, and is watched for rtl_433's "no device" messages; exit code 2 with no output also
/// means no usable dongle.
/// </summary>
public sealed class Rtl433Runner(Rtl433Options options, ReceiverHealth health, TimeProvider time, ILogger<Rtl433Runner> logger)
{
    // What rtl_433 (via librtlsdr) prints when there's no usable dongle: none plugged in, or one it can't open
    // (e.g. claimed by the DVB-T kernel driver, or no permission).
    private static readonly string[] DongleMissingMessages =
    [
        "No supported devices found",
        "Failed to open rtlsdr device",
        "usb_open error",
        "usb_claim_interface error",
    ];

    // With -F json, rtl_433 prints none of those (only with -F log, and then on stdout, mixed with the data).
    // What's left is its exit code: 2, at once, when it finds no usable device.
    internal const int NoDeviceExitCode = 2;

    /// <param name="onLine">Called for each stdout line, in order. It should not throw; if it does, the error is logged and reading carries on.</param>
    public async Task<RunResult> RunOnceAsync(Func<string, Task> onLine, CancellationToken stoppingToken)
    {
        var startedAt = time.GetUtcNow();
        var start = new ProcessStartInfo(options.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in options.ArgumentList)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start '{options.Path}'.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Couldn't start rtl_433 ({Path}): is it installed?", options.Path);
            return Ended(new RunResult(RunEnd.StartFailed, null, false, 0, startedAt, time.GetUtcNow(), ex.Message));
        }

        using var _ = process;
        health.RecordRunStarted();
        logger.LogInformation("rtl_433 started (pid {Pid}): {Path} {Arguments}", process.Id, options.Path, options.Arguments);

        var dongleMissing = false;
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
            {
                if (DongleMissingMessages.Any(message => line.Contains(message, StringComparison.OrdinalIgnoreCase)))
                {
                    dongleMissing = true;
                }

                logger.LogInformation("rtl_433: {Line}", line);
            }
        });

        long lines = 0;
        RunEnd end;
        using var silence = new CancellationTokenSource(options.SilenceTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, silence.Token);
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(linked.Token);
                if (line is null)
                {
                    end = RunEnd.Exited;
                    break;
                }

                silence.CancelAfter(options.SilenceTimeout);
                lines++;
                health.RecordLine();
                try
                {
                    await onLine(line);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Handling an rtl_433 line failed; carrying on");
                }
            }
        }
        catch (OperationCanceledException)
        {
            end = stoppingToken.IsCancellationRequested ? RunEnd.Stopped : RunEnd.Silent;
        }

        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime.
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await stderr;

        dongleMissing |= end == RunEnd.Exited && process.ExitCode == NoDeviceExitCode && lines == 0;
        var result = new RunResult(end, process.ExitCode, dongleMissing, lines, startedAt, time.GetUtcNow());
        switch (end)
        {
            case RunEnd.Exited:
                logger.LogWarning(
                    "rtl_433 exited with code {ExitCode} after {Duration} ({Lines} lines){Hint}",
                    result.ExitCode, result.Duration, lines, dongleMissing ? ": no usable RTL-SDR dongle" : "");
                break;
            case RunEnd.Silent:
                logger.LogWarning("rtl_433 wrote nothing for {Timeout}; killed it", options.SilenceTimeout);
                break;
            default:
                logger.LogInformation("rtl_433 stopped ({Lines} lines)", lines);
                break;
        }

        return Ended(result);
    }

    private RunResult Ended(RunResult result)
    {
        health.RecordRunEnded(result);
        return result;
    }
}
