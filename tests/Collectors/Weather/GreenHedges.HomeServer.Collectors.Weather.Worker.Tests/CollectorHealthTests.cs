using System.Net;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Tests;

public sealed class CollectorHealthTests
{
    private const string PingUrl = "https://hc-ping.com/00000000-0000-0000-0000-000000000000";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly WeatherFields Wind = new(null, null, 1.2, null, null, null, null, null, null, null, null);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly CollectorHealth _health;

    public CollectorHealthTests() => _health = new CollectorHealth(_time);

    [Fact]
    public void Not_healthy_before_a_station_message()
    {
        _health.RecordArchived(_time.GetUtcNow());   // other devices' messages don't count

        Assert.Null(_health.HealthyStatus(_time.GetUtcNow(), Interval));
    }

    [Fact]
    public void Healthy_while_station_messages_arrive_within_the_interval()
    {
        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind);
        _time.Advance(Interval);

        Assert.Contains("Last station message", _health.HealthyStatus(_time.GetUtcNow(), Interval));
    }

    [Fact]
    public void Not_healthy_once_the_last_station_message_is_older_than_the_interval()
    {
        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind);
        _time.Advance(Interval + TimeSpan.FromSeconds(1));

        Assert.Null(_health.HealthyStatus(_time.GetUtcNow(), Interval));
    }

    [Fact]
    public void Battery_low_since_is_kept_across_messages_without_the_flag_and_cleared_by_ok()
    {
        var start = _time.GetUtcNow();
        _health.RecordStationMessage(start, "12345678", Wind with { BatteryOk = 0 });
        _time.Advance(TimeSpan.FromSeconds(12));
        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind);
        _time.Advance(TimeSpan.FromSeconds(12));
        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind with { BatteryOk = 0 });

        Assert.Equal(start, _health.Snapshot().BatteryLowSince);

        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind with { BatteryOk = 1 });
        Assert.Null(_health.Snapshot().BatteryLowSince);
        Assert.Equal(1, _health.Snapshot().LastBatteryOk);
    }

    [Fact]
    public async Task Heartbeat_pings_only_while_the_station_is_heard()
    {
        var handler = new PingHandler();
        var heartbeat = new HeartbeatService(
            new HttpClient(handler),
            _health,
            Options.Create(new HeartbeatOptions { PingUrl = PingUrl, Interval = Interval }),
            _time,
            NullLogger<HeartbeatService>.Instance);

        Assert.False(await heartbeat.PingIfHealthyAsync(CancellationToken.None));

        _health.RecordStationMessage(_time.GetUtcNow(), "12345678", Wind);
        Assert.True(await heartbeat.PingIfHealthyAsync(CancellationToken.None));

        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.False(await heartbeat.PingIfHealthyAsync(CancellationToken.None));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(PingUrl, request.RequestUri?.ToString());
    }

    private sealed class PingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
