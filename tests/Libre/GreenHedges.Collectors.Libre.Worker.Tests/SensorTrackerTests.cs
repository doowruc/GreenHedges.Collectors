using GreenHedges.Collectors.Libre.LibreLinkUp;
using GreenHedges.Collectors.Libre.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.Collectors.Libre.Worker.Tests;

public sealed class SensorTrackerTests : IDisposable
{
    private static readonly DateTimeOffset Activated = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"librecollector-sensor-test-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
    private readonly GlucoseArchive _archive;
    private readonly ListLogger _logger = new();

    public SensorTrackerTests() => _archive = new GlucoseArchive(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private SensorTracker Tracker() =>
        new(_archive, Options.Create(new CollectorOptions()), _time, _logger);

    private static SensorInfo Sensor(string serial, DateTimeOffset activated) =>
        new(serial, activated, 60, 4, $$"""{"sn":"{{serial}}"}""");

    [Fact]
    public async Task First_sensor_then_unchanged_then_changed()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var tracker = Tracker();

        Assert.Equal(SensorObservation.FirstSeen, await tracker.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Equal(SensorObservation.Unchanged, await tracker.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Equal(SensorObservation.None, await tracker.ObserveAsync(null, CancellationToken.None));

        _time.Advance(TimeSpan.FromDays(8));
        var newActivation = Activated.AddDays(15);
        Assert.Equal(SensorObservation.Changed, await tracker.ObserveAsync(Sensor("SN2", newActivation), CancellationToken.None));

        var changed = Assert.Single(_logger.Messages, m => m.StartsWith("Sensor changed"));
        Assert.Contains("SN1 -> SN2", changed);
        Assert.Contains("readings expected from 2026-10-25 09:00:00Z", changed); // activation + 60 min warm-up
        Assert.Contains("expected end 2026-11-09 08:00:00Z", changed);           // activation + 15 days
    }

    [Fact]
    public async Task Restart_with_the_same_sensor_is_not_a_change()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        var afterRestart = Tracker();
        await afterRestart.LogCurrentAsync(CancellationToken.None);

        Assert.Equal(SensorObservation.Unchanged, await afterRestart.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Contains(_logger.Messages, m => m.StartsWith("Current sensor SN1") && m.Contains("expected end 2026-10-25 08:00:00Z") && m.Contains("(7d 22h left)"));
    }

    [Fact]
    public async Task Restart_onto_a_new_sensor_is_a_change()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        Assert.Equal(SensorObservation.Changed, await Tracker().ObserveAsync(Sensor("SN2", Activated.AddDays(15)), CancellationToken.None));
    }

    private sealed class ListLogger : ILogger<SensorTracker>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
