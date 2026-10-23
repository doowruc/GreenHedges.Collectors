using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Common.Tests;

public sealed class AlertBookTests : IDisposable
{
    private static readonly string[] Keys = ["Silent", "Battery"];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender _sender = new();
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"collectors-alerts-{Guid.NewGuid():N}.json");
    private AlertBook _book;

    public AlertBookTests() => _book = NewBook();

    public void Dispose() => File.Delete(_statePath);

    private AlertBook NewBook() => new(
        _sender,
        new AlertStateStore(_statePath, NullLogger<AlertStateStore>.Instance),
        new CollectorIdentity("TestCollector"),
        _time,
        NullLogger<AlertBook>.Instance);

    /// <summary>A fresh process with the same state file.</summary>
    private async Task RestartAsync()
    {
        _book = NewBook();
        await _book.LoadAsync(Keys, CancellationToken.None);
    }

    [Fact]
    public async Task Raising_twice_sends_one_problem_email_with_the_collector_name()
    {
        await _book.RaiseAsync("Silent", "No station messages", "body 1", CancellationToken.None);
        await _book.RaiseAsync("Silent", "No station messages", "body 2", CancellationToken.None);

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("[TestCollector] PROBLEM: No station messages", sent.Subject);
        Assert.Equal("body 1", sent.Body);
        Assert.True(_book.IsOpen("Silent"));
    }

    [Fact]
    public async Task Resolving_sends_resolved_email_with_duration()
    {
        await _book.RaiseAsync("Silent", "No station messages", "body", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(90));

        await _book.ResolveAsync("Silent", CancellationToken.None);

        Assert.Equal("[TestCollector] RESOLVED: No station messages", _sender.Sent[^1].Subject);
        Assert.StartsWith("Resolved after 1h 30m.", _sender.Sent[^1].Body);
        Assert.False(_book.IsOpen("Silent"));
    }

    [Fact]
    public async Task Resolving_an_alert_that_is_not_open_sends_nothing()
    {
        await _book.ResolveAsync("Silent", CancellationToken.None);

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Failed_problem_email_is_retried_and_no_resolved_email_without_it()
    {
        _sender.FailuresRemaining = 1;
        await _book.RaiseAsync("Silent", "No station messages", "body", CancellationToken.None);
        Assert.Empty(_sender.Sent);

        await _book.RaiseAsync("Silent", "No station messages", "body", CancellationToken.None);
        Assert.Single(_sender.Sent);

        _sender.FailuresRemaining = int.MaxValue;
        await _book.RaiseAsync("Battery", "Sensor battery low", "body", CancellationToken.None);
        _sender.FailuresRemaining = 0;
        await _book.ResolveAsync("Battery", CancellationToken.None);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Open_alert_survives_a_restart_without_a_repeat_email()
    {
        await _book.RaiseAsync("Silent", "No station messages", "body", CancellationToken.None);

        await RestartAsync();
        await _book.RaiseAsync("Silent", "No station messages", "body", CancellationToken.None);
        Assert.Single(_sender.Sent);

        await _book.ResolveAsync("Silent", CancellationToken.None);
        Assert.Equal("[TestCollector] RESOLVED: No station messages", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Unknown_saved_keys_are_dropped_on_load()
    {
        await _book.RaiseAsync("Retired", "An alert this version no longer has", "body", CancellationToken.None);

        await RestartAsync();

        Assert.False(_book.IsOpen("Retired"));
    }

    private sealed class RecordingSender : IAlertSender
    {
        public List<(string Subject, string Body)> Sent { get; } = [];

        public int FailuresRemaining { get; set; }

        public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new HttpRequestException("Resend is down");
            }

            Sent.Add((subject, body));
            return Task.CompletedTask;
        }
    }
}
