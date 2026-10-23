using System.Net;
using GreenHedges.HomeServer.Common.Heartbeat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Common.Tests;

public sealed class HeartbeatServiceTests
{
    private const string PingUrl = "https://hc-ping.com/00000000-0000-0000-0000-000000000000";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly PingHandler _handler = new();
    private readonly FakeSource _source = new();
    private readonly HeartbeatService _heartbeat;

    public HeartbeatServiceTests()
    {
        _heartbeat = new HeartbeatService(
            new HttpClient(_handler),
            _source,
            Options.Create(new HeartbeatOptions { PingUrl = PingUrl, Interval = TimeSpan.FromMinutes(5) }),
            _time,
            NullLogger<HeartbeatService>.Instance);
    }

    [Fact]
    public async Task Pings_with_the_sources_status_when_healthy()
    {
        _source.Status = "All good";

        Assert.True(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));

        Assert.Equal(PingUrl, _handler.Uri?.ToString());
        Assert.Equal("All good", _handler.Body);
        Assert.Equal((_time.GetUtcNow(), TimeSpan.FromMinutes(5)), _source.LastAsked);
    }

    [Fact]
    public async Task No_ping_when_the_source_is_not_healthy()
    {
        Assert.False(await _heartbeat.PingIfHealthyAsync(CancellationToken.None));
        Assert.Null(_handler.Uri);
    }

    private sealed class FakeSource : IHeartbeatSource
    {
        public string? Status { get; set; }

        public (DateTimeOffset Now, TimeSpan Interval)? LastAsked { get; private set; }

        public string? HealthyStatus(DateTimeOffset now, TimeSpan interval)
        {
            LastAsked = (now, interval);
            return Status;
        }
    }

    private sealed class PingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
