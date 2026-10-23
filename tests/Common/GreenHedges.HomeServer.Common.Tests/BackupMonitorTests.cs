using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Common.Tests;

public sealed class BackupMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"collectors-backup-alerts-{Guid.NewGuid():N}.json");
    private readonly List<string> _subjects = [];
    private readonly BackupHealth _health;
    private readonly AlertBook _alerts;
    private readonly BackupMonitor _monitor;

    public BackupMonitorTests()
    {
        _health = new BackupHealth(_time);
        _alerts = new AlertBook(
            new RecordingSender(_subjects),
            new AlertStateStore(_statePath, NullLogger<AlertStateStore>.Instance),
            new CollectorIdentity("TestCollector"),
            _time,
            NullLogger<AlertBook>.Instance);
        _monitor = new BackupMonitor(
            _health,
            Options.Create(new BackupOptions { AlertAfter = TimeSpan.FromHours(36) }),
            Options.Create(new OffsiteOptions { Bucket = "bucket", AlertAfter = TimeSpan.FromHours(36) }));
    }

    public void Dispose() => File.Delete(_statePath);

    private Task CheckAsync() => _monitor.CheckAsync(_alerts, _time.GetUtcNow(), "Context", CancellationToken.None);

    [Fact]
    public async Task No_alert_within_the_grace_period_after_startup()
    {
        _time.Advance(TimeSpan.FromHours(35));

        await CheckAsync();

        Assert.Empty(_subjects);
    }

    [Fact]
    public async Task Both_alerts_after_36_hours_without_copies_then_resolve()
    {
        _time.Advance(TimeSpan.FromHours(37));
        await CheckAsync();

        Assert.Equal(["[TestCollector] PROBLEM: Backups failing", "[TestCollector] PROBLEM: Off-site backup failing"], _subjects);

        _health.RecordBackup(_time.GetUtcNow());
        _health.RecordOffsite(_time.GetUtcNow());
        await CheckAsync();

        Assert.Equal(
            ["[TestCollector] RESOLVED: Backups failing", "[TestCollector] RESOLVED: Off-site backup failing"],
            _subjects[2..]);
    }

    [Fact]
    public async Task Misconfigured_offsite_alerts_immediately()
    {
        _health.RecordBackup(_time.GetUtcNow());
        _health.RecordOffsiteFailure(new OffsiteMisconfiguredException("Offsite:Region is not set."));

        await CheckAsync();

        Assert.Equal(["[TestCollector] PROBLEM: Off-site backup failing"], _subjects);
    }

    private sealed class RecordingSender(List<string> subjects) : IAlertSender
    {
        public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            subjects.Add(subject);
            return Task.CompletedTask;
        }
    }
}
