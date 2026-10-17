using System.Text;
using GreenHedges.Collectors.Libre.LibreLinkUp;
using GreenHedges.Collectors.Libre.Nightscout;
using GreenHedges.Collectors.Libre.Worker.Backups;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Libre.Worker.Alerts;

/// <summary>
/// Checks collector health every <see cref="AlertOptions.CheckInterval"/> and emails when a problem
/// starts and when it clears. One email per incident: repeats are suppressed until recovery.
/// </summary>
/// <remarks>
/// Open alerts are saved in <see cref="AlertStateStore"/>, so they survive a restart. Health state starts
/// empty after a restart, so an alert is only resolved on positive evidence gathered since startup
/// (a fresh reading, a successful poll, a recent backup, a successful publish), never because
/// "no error has been seen yet".
/// </remarks>
public sealed class AlertMonitorService(
    CollectorHealth health,
    IAlertSender sender,
    IOptions<AlertOptions> options,
    IOptions<BackupOptions> backupOptions,
    IOptions<NightscoutOptions> nightscoutOptions,
    IOptions<OffsiteOptions> offsiteOptions,
    AlertStateStore store,
    TimeProvider time,
    ILogger<AlertMonitorService> logger) : BackgroundService
{
    private const string SubjectPrefix = "[LibreCollector]";

    private readonly AlertOptions _options = options.Value;
    private readonly BackupOptions _backupOptions = backupOptions.Value;
    private readonly NightscoutOptions _nightscoutOptions = nightscoutOptions.Value;
    private readonly OffsiteOptions _offsiteOptions = offsiteOptions.Value;
    private readonly Dictionary<AlertKind, ActiveAlert> _active = [];

    private enum AlertKind
    {
        /// <summary>LibreLinkUp needs the user to act (credentials, terms, app version, sharing).</summary>
        UserAction,

        /// <summary>No new current reading for longer than <see cref="AlertOptions.StaleAfter"/>.</summary>
        Stale,

        /// <summary>No good backup for longer than <see cref="BackupOptions.AlertAfter"/>.</summary>
        Backup,

        /// <summary>Nightscout publishing failing for longer than <see cref="AlertOptions.PublishStalledAfter"/>.</summary>
        Publish,

        /// <summary>No successful off-site copy for longer than <see cref="OffsiteOptions.AlertAfter"/>.</summary>
        Offsite,
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EmailEnabled)
        {
            logger.LogWarning("Alerts:ResendApiKey is not set; alerts will only be logged");
        }

        await LoadStateAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.CheckInterval, time);
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
                    logger.LogError(ex, "Alert check failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Restores alerts that were open when the collector last stopped.</summary>
    internal async Task LoadStateAsync(CancellationToken cancellationToken)
    {
        _active.Clear();
        foreach (var (key, saved) in await store.LoadAsync(cancellationToken))
        {
            if (Enum.TryParse<AlertKind>(key, out var kind))
            {
                _active[kind] = new ActiveAlert(saved.Since) { Notified = saved.Notified };
                logger.LogInformation(
                    "Alert still open from before restart: {Title} (since {Since:u}, emailed: {Notified})",
                    saved.Title,
                    saved.Since,
                    saved.Notified);
            }
        }
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();
        var now = time.GetUtcNow();

        if (snapshot.LastError is LibreLinkUpUserActionRequiredException userAction)
        {
            await RaiseAsync(
                AlertKind.UserAction,
                "LibreLinkUp needs attention",
                $"""
                Collection is paused (it retries periodically) until this is fixed:

                {userAction.Message}

                {Describe(snapshot, now)}
                """,
                cancellationToken);
        }
        else if (snapshot.LastSuccessAt is not null)
        {
            await ResolveAsync(AlertKind.UserAction, "LibreLinkUp needs attention", cancellationToken);
        }

        // Measured from startup too, so a restart gets a grace period rather than an instant alert.
        var latest = snapshot.LatestReadingAt is { } readingAt && readingAt > snapshot.StartedAt
            ? readingAt
            : snapshot.StartedAt;
        var age = now - latest;

        if (age > _options.StaleAfter)
        {
            // While a user-action alert is open it already explains the missing data.
            if (!_active.ContainsKey(AlertKind.UserAction))
            {
                await RaiseAsync(
                    AlertKind.Stale,
                    "No new glucose readings",
                    $"""
                    No new glucose reading for {FormatDuration(age)}.

                    {Describe(snapshot, now)}
                    """,
                    cancellationToken);
            }
        }
        else if (snapshot.LatestReadingAt is { } fresh && now - fresh <= _options.StaleAfter)
        {
            // A real recent reading, not just the startup grace period.
            await ResolveAsync(AlertKind.Stale, "No new glucose readings", cancellationToken);
        }

        if (_backupOptions.Enabled)
        {
            // Measured from startup when there's no backup at all, like the stale-data check.
            var lastBackup = snapshot.LastBackupAt ?? snapshot.StartedAt;
            var sinceBackup = now - lastBackup;
            if (sinceBackup > _backupOptions.AlertAfter)
            {
                await RaiseAsync(
                    AlertKind.Backup,
                    "Backups failing",
                    $"""
                    No successful backup for {FormatDuration(sinceBackup)}.
                    Last good backup: {(snapshot.LastBackupAt is { } b ? $"{b:u}" : "none")}
                    Last backup error: {(snapshot.LastBackupError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                    {Describe(snapshot, now)}
                    """,
                    cancellationToken);
            }
            else if (snapshot.LastBackupAt is { } goodBackup && now - goodBackup <= _backupOptions.AlertAfter)
            {
                await ResolveAsync(AlertKind.Backup, "Backups failing", cancellationToken);
            }
        }
        else
        {
            await ResolveAsync(AlertKind.Backup, "Backups failing", cancellationToken);
        }

        if (snapshot.PublishFailingSince is { } failingSince && now - failingSince > _options.PublishStalledAfter)
        {
            await RaiseAsync(
                AlertKind.Publish,
                "Nightscout publishing stalled",
                $"""
                Publishing to Nightscout has been failing for {FormatDuration(now - failingSince)}.
                Readings are still being collected and archived; Nightscout will catch up once it's reachable.
                Last error: {(snapshot.LastPublishError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                {Describe(snapshot, now)}
                """,
                cancellationToken);
        }
        else if (!_nightscoutOptions.Enabled
                 || (snapshot.PublishFailingSince is null && snapshot.LastPublishSuccessAt is not null))
        {
            await ResolveAsync(AlertKind.Publish, "Nightscout publishing stalled", cancellationToken);
        }

        if (_offsiteOptions.Enabled && _backupOptions.Enabled)
        {
            var lastOffsite = snapshot.LastOffsiteAt ?? snapshot.StartedAt;
            var sinceOffsite = now - lastOffsite;
            // Misconfiguration can't fix itself, so it alerts straight away rather than after AlertAfter.
            if (sinceOffsite > _offsiteOptions.AlertAfter || snapshot.LastOffsiteError is OffsiteMisconfiguredException)
            {
                await RaiseAsync(
                    AlertKind.Offsite,
                    "Off-site backup failing",
                    $"""
                    No successful off-site backup copy for {FormatDuration(sinceOffsite)}.
                    Last good copy: {(snapshot.LastOffsiteAt is { } o ? $"{o:u}" : "none")}
                    Last error: {(snapshot.LastOffsiteError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}
                    Local backups are unaffected.

                    {Describe(snapshot, now)}
                    """,
                    cancellationToken);
            }
            else if (snapshot.LastOffsiteAt is { } goodOffsite && now - goodOffsite <= _offsiteOptions.AlertAfter)
            {
                await ResolveAsync(AlertKind.Offsite, "Off-site backup failing", cancellationToken);
            }
        }
        else
        {
            await ResolveAsync(AlertKind.Offsite, "Off-site backup failing", cancellationToken);
        }
    }

    private async Task RaiseAsync(AlertKind kind, string title, string body, CancellationToken cancellationToken)
    {
        var changed = false;
        if (!_active.TryGetValue(kind, out var alert))
        {
            alert = new ActiveAlert(time.GetUtcNow());
            _active[kind] = alert;
            changed = true;
            logger.LogWarning("Alert raised: {Title}", title);
        }

        if (!alert.Notified)
        {
            // If sending fails, Notified stays false and the next check tries again.
            alert.Notified = await TrySendAsync($"{SubjectPrefix} PROBLEM: {title}", body, cancellationToken);
            changed |= alert.Notified;
        }

        if (changed)
        {
            await SaveStateAsync(cancellationToken);
        }
    }

    private async Task ResolveAsync(AlertKind kind, string title, CancellationToken cancellationToken)
    {
        if (!_active.Remove(kind, out var alert))
        {
            return;
        }

        await SaveStateAsync(cancellationToken);
        var duration = time.GetUtcNow() - alert.Since;
        logger.LogInformation("Alert resolved after {Duration}: {Title}", duration, title);

        // Only send a recovery email if the problem email went out. Not retried if it fails.
        if (alert.Notified)
        {
            await TrySendAsync(
                $"{SubjectPrefix} RESOLVED: {title}",
                $"Resolved after {FormatDuration(duration)}. Collection is back to normal on {Environment.MachineName}.",
                cancellationToken);
        }
    }

    private Task SaveStateAsync(CancellationToken cancellationToken) =>
        store.SaveAsync(
            _active.ToDictionary(
                pair => pair.Key.ToString(),
                pair => new PersistedAlert(Title(pair.Key), pair.Value.Since, pair.Value.Notified)),
            cancellationToken);

    private static string Title(AlertKind kind) => kind switch
    {
        AlertKind.UserAction => "LibreLinkUp needs attention",
        AlertKind.Stale => "No new glucose readings",
        AlertKind.Backup => "Backups failing",
        AlertKind.Publish => "Nightscout publishing stalled",
        AlertKind.Offsite => "Off-site backup failing",
        _ => kind.ToString(),
    };

    private async Task<bool> TrySendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to send alert: {Subject}", subject);
            return false;
        }
    }

    private static string Describe(CollectorHealthSnapshot snapshot, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Host: {Environment.MachineName}");
        text.AppendLine($"Now: {now:u}");
        text.AppendLine($"Collector started: {snapshot.StartedAt:u}");
        text.AppendLine($"Newest reading (sensor time): {(snapshot.LatestReadingAt is { } r ? $"{r:u}" : "none since startup")}");
        text.AppendLine($"Last successful poll: {(snapshot.LastSuccessAt is { } s ? $"{s:u}" : "none since startup")}");
        if (snapshot.LastError is { } error)
        {
            text.AppendLine($"Last error ({snapshot.LastErrorAt:u}): {error.GetType().Name}: {error.Message}");
        }

        return text.ToString();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{(int)duration.TotalMinutes}m";

    private sealed class ActiveAlert(DateTimeOffset since)
    {
        public DateTimeOffset Since { get; } = since;

        public bool Notified { get; set; }
    }
}
