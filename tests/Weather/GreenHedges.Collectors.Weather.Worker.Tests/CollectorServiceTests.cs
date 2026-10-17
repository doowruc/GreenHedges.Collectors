using GreenHedges.Collectors.Weather.Rtl433;
using GreenHedges.Collectors.Weather.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.Collectors.Weather.Worker.Tests;

public sealed class CollectorServiceTests : IDisposable
{
    private const string StationTemperature =
        """{"time" : "1792234567.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "battery_ok" : 0, "temperature_C" : 11.6, "humidity" : 87, "wind_avg_m_s" : 0.9, "flags" : 0}""";

    private const string StationRain =
        """{"time" : "1792234579.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "rain_mm" : 998.4, "wind_avg_m_s" : 1.4, "flags" : 1}""";

    private const string Neighbour =
        """{"time" : "1792234570.000000", "protocol" : 172, "model" : "Bresser-6in1", "id" : 87654321, "channel" : 0, "temperature_C" : 14.0}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-worker-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly WeatherArchive _archive;
    private readonly CollectorHealth _health;
    private readonly CollectorService _service;

    public CollectorServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        _archive.InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
        _health = new CollectorHealth(_time);

        var rtl433 = new Rtl433Options();
        var receiver = new ReceiverHealth(_time);
        _service = new CollectorService(
            new Rtl433Supervisor(
                new Rtl433Runner(rtl433, receiver, _time, NullLogger<Rtl433Runner>.Instance),
                rtl433,
                _time,
                NullLogger<Rtl433Supervisor>.Instance),
            _archive,
            _health,
            Options.Create(new StationOptions { DeviceIds = ["12345678"] }),
            _time,
            NullLogger<CollectorService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Station_lines_are_archived_typed_and_recorded_in_health()
    {
        await _service.HandleLineAsync(StationTemperature, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(12));
        await _service.HandleLineAsync(StationRain, CancellationToken.None);

        var latest = await _archive.GetLatestStationReadingAsync(CancellationToken.None);
        Assert.Equal(998.4, latest!.Weather.RainMm);
        Assert.Equal(_time.GetUtcNow(), latest.ReceivedAtUtc);

        var health = _health.Snapshot();
        Assert.Equal(_time.GetUtcNow(), health.LastStationMessageAt);
        Assert.Equal("12345678", health.LastStationDeviceId);
        Assert.Equal(0, health.LastBatteryOk);   // from the temperature message; the rain message has none
    }

    [Fact]
    public async Task Other_devices_and_invalid_lines_are_archived_but_not_the_station()
    {
        await _service.HandleLineAsync(Neighbour, CancellationToken.None);
        await _service.HandleLineAsync("garbled", CancellationToken.None);

        Assert.Equal(2, await _archive.CountMessagesAsync(CancellationToken.None));
        Assert.Null(await _archive.GetLatestStationReadingAsync(CancellationToken.None));
        Assert.Null(_health.Snapshot().LastStationMessageAt);
        Assert.Equal(_time.GetUtcNow(), _health.Snapshot().LastArchivedAt);
    }

    [Fact]
    public async Task Archive_failure_is_recorded_not_thrown()
    {
        var broken = new WeatherArchive(Path.Combine(_dir, "missing-folder", "weather.db"));
        var service = new CollectorService(
            null!,
            broken,
            _health,
            Options.Create(new StationOptions()),
            _time,
            NullLogger<CollectorService>.Instance);

        await service.HandleLineAsync(StationTemperature, CancellationToken.None);

        Assert.NotNull(_health.Snapshot().LastArchiveError);
    }

    [Theory]
    [InlineData("Bresser-6in1", "12345678", true)]
    [InlineData("Bresser-6in1", "99999999", true)]
    [InlineData("Bresser-6in1", "87654321", false)]
    [InlineData("Bresser-5in1", "12345678", false)]
    [InlineData(null, "12345678", false)]
    [InlineData("Bresser-6in1", null, false)]
    public void Station_is_matched_on_model_and_any_configured_id(string? model, string? id, bool expected)
    {
        var station = new StationOptions { DeviceIds = ["12345678", "99999999"] };

        Assert.Equal(expected, station.IsStation(model, id));
    }
}
