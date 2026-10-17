using System.IO.Compression;
using GreenHedges.Collectors.Libre.Storage;
using GreenHedges.Collectors.Libre.Worker.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.Collectors.Libre.Worker.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"librecollector-backup-test-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero));
    private readonly CollectorHealth _health;
    private readonly GlucoseArchive _archive;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_root);
        _health = new CollectorHealth(_time);
        _archive = new GlucoseArchive(Path.Combine(_root, "live.db"));
        _service = new BackupService(
            _archive,
            _health,
            Options.Create(new BackupOptions { Directory = Path.Combine(_root, "backups"), Retain = 3 }),
            _time,
            NullLogger<BackupService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Backup_is_a_gzipped_valid_copy_and_is_recorded()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [new ArchivedReading(_time.GetUtcNow(), ReadingSource.Current, null, 115, 3, 1, false, false, _time.GetUtcNow(), "{}")],
            CancellationToken.None);

        var path = await _service.RunBackupAsync(CancellationToken.None);

        Assert.NotNull(path);
        Assert.Equal("librecollector-20261017T023000Z.db.gz", Path.GetFileName(path));
        Assert.Equal(_time.GetUtcNow(), _health.Snapshot().LastBackupAt);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "backups")));

        var restored = Path.Combine(_root, "restored.db");
        await using (var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        await using (var output = File.Create(restored))
        {
            await input.CopyToAsync(output);
        }

        var copy = new GlucoseArchive(restored);
        Assert.Equal(1, await copy.CountAsync(ReadingSource.Current, CancellationToken.None));
    }

    [Fact]
    public async Task Keeps_only_the_newest_backups()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        for (var day = 0; day < 5; day++)
        {
            await _service.RunBackupAsync(CancellationToken.None);
            _time.Advance(TimeSpan.FromDays(1));
        }

        string[] names = [.. Directory.GetFiles(Path.Combine(_root, "backups")).Select(f => Path.GetFileName(f)!).Order()];
        Assert.Equal(
            ["librecollector-20261019T023000Z.db.gz", "librecollector-20261020T023000Z.db.gz", "librecollector-20261021T023000Z.db.gz"],
            names);
    }

    [Fact]
    public async Task Failure_is_recorded_not_thrown()
    {
        // No InitialiseAsync and a directory that can't be created: the backup must fail cleanly.
        var blocker = Path.Combine(_root, "blocked");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var service = new BackupService(
            _archive,
            _health,
            Options.Create(new BackupOptions { Directory = Path.Combine(blocker, "backups") }),
            _time,
            NullLogger<BackupService>.Instance);

        Assert.Null(await service.RunBackupAsync(CancellationToken.None));
        Assert.NotNull(_health.Snapshot().LastBackupError);
        Assert.Null(_health.Snapshot().LastBackupAt);
    }

    [Theory]
    // 01:00 BST (00:00Z) on 17 Oct → 03:30 BST same day = 02:30Z
    [InlineData("2026-10-17T00:00:00Z", "2026-10-17T02:30:00Z")]
    // Just after today's run → tomorrow
    [InlineData("2026-10-17T02:30:01Z", "2026-10-18T02:30:00Z")]
    // Clocks go back 25 Oct 2026: 03:30 GMT = 03:30Z
    [InlineData("2026-10-24T12:00:00Z", "2026-10-25T03:30:00Z")]
    // Clocks go forward 29 Mar 2026: 03:30 BST = 02:30Z
    [InlineData("2026-03-28T12:00:00Z", "2026-03-29T02:30:00Z")]
    public void Next_run_is_the_next_local_time_of_day(string now, string expected)
    {
        var next = BackupService.NextRun(DateTimeOffset.Parse(now), new TimeSpan(3, 30, 0), London);

        Assert.Equal(DateTimeOffset.Parse(expected), next);
    }

    [Fact]
    public void Time_skipped_by_clocks_going_forward_runs_an_hour_later()
    {
        // 01:30 local doesn't exist on 29 Mar 2026 (01:00 → 02:00 BST): run at 02:30 BST = 01:30Z.
        var next = BackupService.NextRun(DateTimeOffset.Parse("2026-03-28T12:00:00Z"), new TimeSpan(1, 30, 0), London);

        Assert.Equal(DateTimeOffset.Parse("2026-03-29T01:30:00Z"), next);
    }
}
