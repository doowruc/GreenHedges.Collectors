using System.Globalization;
using System.IO.Compression;
using GreenHedges.Collectors.Libre.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Libre.Worker.Backups;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; } = true;

    /// <summary>Folder for backups. A relative path is resolved against the working directory, like the database.</summary>
    public string Directory { get; set; } = "backups";

    /// <summary>Local time of day (host time zone) to take the daily backup.</summary>
    public TimeSpan TimeOfDay { get; set; } = new(3, 30, 0);

    /// <summary>How many backups to keep locally. Older ones are deleted after each successful backup.</summary>
    public int Retain { get; set; } = 7;

    /// <summary>Raise the "Backups failing" alert if there's been no good backup for this long.</summary>
    public TimeSpan AlertAfter { get; set; } = TimeSpan.FromHours(36);
}

/// <summary>
/// Daily consistent, integrity-checked, gzipped copy of the SQLite archive, kept for
/// <see cref="BackupOptions.Retain"/> days. Off-machine copies are pulled from this folder by the Mac.
/// </summary>
public sealed class BackupService(
    GlucoseArchive archive,
    CollectorHealth health,
    IOptions<BackupOptions> options,
    TimeProvider time,
    ILogger<BackupService> logger) : BackgroundService
{
    private const string FilePrefix = "librecollector-";
    private const string FileSuffix = ".db.gz";
    private const string FileTimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    private readonly BackupOptions _options = options.Value;
    private readonly string _directory = Path.GetFullPath(options.Value.Directory);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Backups are disabled (Backup:Enabled = false)");
            return;
        }

        Directory.CreateDirectory(_directory);

        // Count an existing recent backup, so a restart doesn't raise the alert or force an immediate backup.
        var newest = ListBackups(_directory).FirstOrDefault();
        if (newest is not null)
        {
            health.RecordBackup(newest.CreatedAt);
        }

        var now = time.GetUtcNow();
        if (newest is null || now - newest.CreatedAt > TimeSpan.FromDays(1))
        {
            await RunBackupAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = NextRun(time.GetUtcNow(), _options.TimeOfDay, time.LocalTimeZone);
            logger.LogDebug("Next backup at {Next:u}", next);
            try
            {
                await Task.Delay(next - time.GetUtcNow(), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunBackupAsync(stoppingToken);
        }
    }

    /// <summary>Takes one backup and prunes old ones. Never throws (except on shutdown); failures go to health and the log.</summary>
    internal async Task<string?> RunBackupAsync(CancellationToken cancellationToken)
    {
        var startedAt = time.GetUtcNow();
        var finalPath = Path.Combine(
            _directory,
            FilePrefix + startedAt.UtcDateTime.ToString(FileTimestampFormat, CultureInfo.InvariantCulture) + FileSuffix);
        var dbTemp = Path.ChangeExtension(finalPath, ".snapshot.tmp");
        var gzTemp = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(_directory);
            await archive.BackupToAsync(dbTemp, cancellationToken);

            await using (var input = File.OpenRead(dbTemp))
            await using (var output = File.Create(gzTemp))
            await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            {
                await input.CopyToAsync(gzip, cancellationToken);
            }

            File.Move(gzTemp, finalPath, overwrite: true);
            var size = new FileInfo(finalPath).Length;

            health.RecordBackup(time.GetUtcNow());
            logger.LogInformation("Backup written: {Path} ({SizeKiB:N0} KiB)", finalPath, size / 1024);

            Prune();
            return finalPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            health.RecordBackupFailure(ex);
            logger.LogError(ex, "Backup failed");
            return null;
        }
        finally
        {
            TryDelete(dbTemp);
            TryDelete(gzTemp);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Couldn't delete temporary file {Path}", path);
        }
    }

    private void Prune()
    {
        foreach (var old in ListBackups(_directory).Skip(Math.Max(1, _options.Retain)))
        {
            try
            {
                File.Delete(old.Path);
                logger.LogInformation("Deleted old backup {Path}", old.Path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't delete old backup {Path}", old.Path);
            }
        }
    }

    /// <summary>Backups in <paramref name="directory"/>, newest first, timed by the timestamp in the file name.</summary>
    internal static IEnumerable<BackupFile> ListBackups(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, FilePrefix + "*" + FileSuffix)
                .Select(path => (path, name: Path.GetFileName(path)))
                .Select(f => DateTimeOffset.TryParseExact(
                        f.name[FilePrefix.Length..^FileSuffix.Length],
                        FileTimestampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var createdAt)
                    ? new BackupFile(f.path, createdAt)
                    : null)
                .OfType<BackupFile>()
                .OrderByDescending(b => b.CreatedAt)
            : [];

    /// <summary>The next occurrence of <paramref name="timeOfDay"/> in <paramref name="zone"/> strictly after <paramref name="now"/>.</summary>
    internal static DateTimeOffset NextRun(DateTimeOffset now, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        for (var day = localNow.Date; ; day = day.AddDays(1))
        {
            var local = day + timeOfDay;
            if (zone.IsInvalidTime(local))
            {
                // Skipped by a clocks-forward change: run an hour later instead.
                local = local.AddHours(1);
            }

            var candidate = new DateTimeOffset(local, zone.GetUtcOffset(local));
            if (candidate > now)
            {
                return candidate;
            }
        }
    }

    internal sealed record BackupFile(string Path, DateTimeOffset CreatedAt);
}
