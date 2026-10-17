using System.Text;
using GreenHedges.Collectors.Common.Alerts;
using GreenHedges.Collectors.Libre.LibreLinkUp;
using GreenHedges.Collectors.Libre.Nightscout;
using GreenHedges.Collectors.Libre.Worker.Backups;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Libre.Worker.Alerts;

/// <summary>
/// The Libre collector's checks: LibreLinkUp needing attention, stale readings, backups, Nightscout
/// publishing and off-site copies. Emails and their persistence are the shared <see cref="AlertBook"/>'s.
/// </summary>
public sealed class LibreAlertMonitor(
    CollectorHealth health,
    AlertBook alerts,
    IOptions<AlertOptions> alertOptions,
    IOptions<LibreAlertOptions> options,
    IOptions<BackupOptions> backupOptions,
    IOptions<NightscoutOptions> nightscoutOptions,
    IOptions<OffsiteOptions> offsiteOptions,
    TimeProvider time,
    ILogger<LibreAlertMonitor> logger) : AlertMonitorService(alerts, alertOptions, time, logger)
{
    private static readonly string[] Keys = Enum.GetNames<AlertKind>();

    private readonly LibreAlertOptions _options = options.Value;
    private readonly BackupOptions _backupOptions = backupOptions.Value;
    private readonly NightscoutOptions _nightscoutOptions = nightscoutOptions.Value;
    private readonly OffsiteOptions _offsiteOptions = offsiteOptions.Value;

    private enum AlertKind
    {
        /// <summary>LibreLinkUp needs the user to act (credentials, terms, app version, sharing).</summary>
        UserAction,

        /// <summary>No new current reading for longer than <see cref="LibreAlertOptions.StaleAfter"/>.</summary>
        Stale,

        /// <summary>No good backup for longer than <see cref="BackupOptions.AlertAfter"/>.</summary>
        Backup,

        /// <summary>Nightscout publishing failing for longer than <see cref="LibreAlertOptions.PublishStalledAfter"/>.</summary>
        Publish,

        /// <summary>No successful off-site copy for longer than <see cref="OffsiteOptions.AlertAfter"/>.</summary>
        Offsite,
    }

    protected override IReadOnlyCollection<string> AlertKeys => Keys;

    public override async Task CheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();
        var now = Time.GetUtcNow();

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
            await ResolveAsync(AlertKind.UserAction, cancellationToken);
        }

        // Measured from startup too, so a restart gets a grace period rather than an instant alert.
        var latest = snapshot.LatestReadingAt is { } readingAt && readingAt > snapshot.StartedAt
            ? readingAt
            : snapshot.StartedAt;
        var age = now - latest;

        if (age > _options.StaleAfter)
        {
            // While a user-action alert is open it already explains the missing data.
            if (!Alerts.IsOpen(nameof(AlertKind.UserAction)))
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
            await ResolveAsync(AlertKind.Stale, cancellationToken);
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
                await ResolveAsync(AlertKind.Backup, cancellationToken);
            }
        }
        else
        {
            await ResolveAsync(AlertKind.Backup, cancellationToken);
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
            await ResolveAsync(AlertKind.Publish, cancellationToken);
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
                await ResolveAsync(AlertKind.Offsite, cancellationToken);
            }
        }
        else
        {
            await ResolveAsync(AlertKind.Offsite, cancellationToken);
        }
    }

    private Task RaiseAsync(AlertKind kind, string title, string body, CancellationToken cancellationToken) =>
        Alerts.RaiseAsync(kind.ToString(), title, body, cancellationToken);

    private Task ResolveAsync(AlertKind kind, CancellationToken cancellationToken) =>
        Alerts.ResolveAsync(kind.ToString(), cancellationToken);

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

    private static string FormatDuration(TimeSpan duration) => AlertBook.FormatDuration(duration);
}
