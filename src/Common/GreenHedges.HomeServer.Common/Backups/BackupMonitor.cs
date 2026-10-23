using GreenHedges.HomeServer.Common.Alerts;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Common.Backups;

/// <summary>
/// The backup alerts every collector shares: "Backups failing" and "Off-site backup failing". A
/// collector's <see cref="AlertMonitorService"/> calls <see cref="CheckAsync"/> and includes
/// <see cref="Keys"/> in its own keys.
/// </summary>
public sealed class BackupMonitor(
    BackupHealth health,
    IOptions<BackupOptions> backupOptions,
    IOptions<OffsiteOptions> offsiteOptions)
{
    public const string BackupKey = "Backup";
    public const string OffsiteKey = "Offsite";

    public static IReadOnlyCollection<string> Keys { get; } = [BackupKey, OffsiteKey];

    private readonly BackupOptions _backupOptions = backupOptions.Value;
    private readonly OffsiteOptions _offsiteOptions = offsiteOptions.Value;

    /// <summary>Raises or resolves both alerts. <paramref name="context"/> is the collector's status, appended to each email.</summary>
    public async Task CheckAsync(AlertBook alerts, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();

        if (_backupOptions.Enabled)
        {
            // Measured from startup when there's no backup at all, so a restart gets a grace period.
            var lastBackup = snapshot.LastBackupAt ?? snapshot.StartedAt;
            var sinceBackup = now - lastBackup;
            if (sinceBackup > _backupOptions.AlertAfter)
            {
                await alerts.RaiseAsync(
                    BackupKey,
                    "Backups failing",
                    $"""
                    No successful backup for {AlertBook.FormatDuration(sinceBackup)}.
                    Last good backup: {(snapshot.LastBackupAt is { } b ? $"{b:u}" : "none")}
                    Last backup error: {(snapshot.LastBackupError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                    {context}
                    """,
                    cancellationToken);
            }
            else if (snapshot.LastBackupAt is { } goodBackup && now - goodBackup <= _backupOptions.AlertAfter)
            {
                await alerts.ResolveAsync(BackupKey, cancellationToken);
            }
        }
        else
        {
            await alerts.ResolveAsync(BackupKey, cancellationToken);
        }

        if (_offsiteOptions.Enabled && _backupOptions.Enabled)
        {
            var lastOffsite = snapshot.LastOffsiteAt ?? snapshot.StartedAt;
            var sinceOffsite = now - lastOffsite;
            // Misconfiguration can't fix itself, so it alerts straight away rather than after AlertAfter.
            if (sinceOffsite > _offsiteOptions.AlertAfter || snapshot.LastOffsiteError is OffsiteMisconfiguredException)
            {
                await alerts.RaiseAsync(
                    OffsiteKey,
                    "Off-site backup failing",
                    $"""
                    No successful off-site backup copy for {AlertBook.FormatDuration(sinceOffsite)}.
                    Last good copy: {(snapshot.LastOffsiteAt is { } o ? $"{o:u}" : "none")}
                    Last error: {(snapshot.LastOffsiteError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}
                    Local backups are unaffected.

                    {context}
                    """,
                    cancellationToken);
            }
            else if (snapshot.LastOffsiteAt is { } goodOffsite && now - goodOffsite <= _offsiteOptions.AlertAfter)
            {
                await alerts.ResolveAsync(OffsiteKey, cancellationToken);
            }
        }
        else
        {
            await alerts.ResolveAsync(OffsiteKey, cancellationToken);
        }
    }
}
