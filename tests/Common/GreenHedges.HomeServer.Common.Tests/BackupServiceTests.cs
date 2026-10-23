using System.IO.Compression;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Common.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"collectors-backup-test-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero));
    private readonly BackupHealth _health;
    private readonly FakeSource _source = new();
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_root);
        _health = new BackupHealth(_time);
        _service = new BackupService(
            _source,
            _health,
            Options.Create(new BackupOptions { Directory = Path.Combine(_root, "backups"), Retain = 3 }),
            new CollectorIdentity("LibreCollector"),
            _time,
            NullLogger<BackupService>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Backup_is_a_gzipped_copy_named_for_the_collector_and_is_recorded()
    {
        var path = await _service.RunBackupAsync(CancellationToken.None);

        Assert.NotNull(path);
        Assert.Equal("librecollector-20261017T023000Z.db.gz", Path.GetFileName(path));
        Assert.Equal(_time.GetUtcNow(), _health.Snapshot().LastBackupAt);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "backups")));

        await using var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(input);
        Assert.Equal(FakeSource.Content, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Keeps_only_the_newest_backups()
    {
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
        // A directory that can't be created: the backup must fail cleanly.
        var blocker = Path.Combine(_root, "blocked");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var service = new BackupService(
            _source,
            _health,
            Options.Create(new BackupOptions { Directory = Path.Combine(blocker, "backups") }),
            new CollectorIdentity("LibreCollector"),
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

    [Fact]
    public async Task Source_failure_is_recorded_and_leaves_no_temporary_files()
    {
        _source.Fail = true;

        Assert.Null(await _service.RunBackupAsync(CancellationToken.None));
        Assert.IsType<IOException>(_health.Snapshot().LastBackupError);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "backups")));
    }

    [Fact]
    public async Task A_source_names_its_files_extension_and_they_are_listed_and_pruned_like_any_other()
    {
        var directory = Path.Combine(_root, "sqlserver");
        var service = new BackupService(
            new FakeSqlServerSource(),
            _health,
            Options.Create(new BackupOptions { Directory = directory, Retain = 2 }),
            new CollectorIdentity("VehicleManager"),
            _time,
            NullLogger<BackupService>.Instance);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "vehiclemanager-notes.gz"), "not a backup");

        for (var day = 0; day < 3; day++)
        {
            Assert.EndsWith(".bak.gz", await service.RunBackupAsync(CancellationToken.None));
            _time.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(
            ["vehiclemanager-20261018T023000Z.bak.gz", "vehiclemanager-20261019T023000Z.bak.gz"],
            BackupService.ListBackups(directory, "vehiclemanager-").Select(b => Path.GetFileName(b.Path)).Order());
        Assert.True(File.Exists(Path.Combine(directory, "vehiclemanager-notes.gz"))); // not a backup, so left alone
    }

    private sealed class FakeSqlServerSource : IBackupSource
    {
        public string FileExtension => ".bak";

        public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(destinationPath, "TAPE (pretend)", cancellationToken);
    }

    private sealed class FakeSource : IBackupSource
    {
        public const string Content = "SQLite format 3 (pretend)";

        public bool Fail { get; set; }

        public async Task BackupToAsync(string destinationPath, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("Disk full");
            }

            await File.WriteAllTextAsync(destinationPath, Content, cancellationToken);
        }
    }
}
