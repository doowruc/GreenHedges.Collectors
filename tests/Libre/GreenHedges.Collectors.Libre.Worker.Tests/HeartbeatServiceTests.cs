using System.Net;
using GreenHedges.Collectors.Common.Heartbeat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.Collectors.Libre.Worker.Tests;

public sealed class HeartbeatServiceTests
{
    private const string PingUrl = "https://hc-ping.com/00000000-0000-0000-0000-000000000000";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly PingHandler _handler = new();
    private readonly CollectorHealth _health;
    private readonly HeartbeatService _heartbeat;

    public HeartbeatServiceTests()
    {
        _health = new CollectorHealth(_time);
        _heartbeat = new HeartbeatService(
            new HttpClient(_handler),
            _health,
            Options.Create(new HeartbeatOptions { PingUrl = PingUrl, Interval = TimeSpan.FromMinutes(5) }),
            _time,
            NullLogger<HeartbeatService>.Instance);
    }

    [Fact]
    public async Task Pings_after_a_recent_successful_poll()
    {
        _health.RecordReading(_time.GetUtcNow().AddMinutes(-1));
        _health.RecordSuccess();
        _time.Advance(TimeSpan.FromMinutes(4));

        Assert.True(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(PingUrl, request.RequestUri?.ToString());
    }

    [Fact]
    public async Task No_ping_before_first_successful_poll()
    {
        Assert.False(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task No_ping_when_last_success_is_older_than_interval()
    {
        _health.RecordSuccess();
        _time.Advance(TimeSpan.FromMinutes(6));
        _health.RecordFailure(new HttpRequestException("network down"));

        Assert.False(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Ping_failure_is_swallowed()
    {
        _health.RecordSuccess();
        _handler.Throw = true;

        Assert.False(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Non_success_status_is_reported_as_failed_ping()
    {
        _health.RecordSuccess();
        _handler.Status = HttpStatusCode.NotFound;

        Assert.False(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));
    }

    private sealed class PingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public bool Throw { get; set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new HttpRequestException("hc-ping.com unreachable");
            }

            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }
}
