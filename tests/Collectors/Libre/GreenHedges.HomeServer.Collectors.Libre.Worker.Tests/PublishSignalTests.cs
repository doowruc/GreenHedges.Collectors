using GreenHedges.HomeServer.Collectors.Libre.Worker.Publishing;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Tests;

public sealed class PublishSignalTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly PublishSignal _signal = new();

    [Fact]
    public async Task Notify_wakes_a_waiter_immediately()
    {
        _signal.Notify();

        var wait = _signal.WaitAsync(TimeSpan.FromMinutes(5), _time, CancellationToken.None);

        Assert.True(wait.IsCompleted);
        await wait;
    }

    [Fact]
    public async Task Many_notifies_coalesce_into_one_wake()
    {
        _signal.Notify();
        _signal.Notify();
        _signal.Notify();

        await _signal.WaitAsync(TimeSpan.FromMinutes(5), _time, CancellationToken.None);
        var second = _signal.WaitAsync(TimeSpan.FromMinutes(5), _time, CancellationToken.None);

        Assert.False(second.IsCompleted);
        _time.Advance(TimeSpan.FromMinutes(5));
        await second;
    }

    [Fact]
    public async Task Times_out_without_a_notify()
    {
        var wait = _signal.WaitAsync(TimeSpan.FromMinutes(5), _time, CancellationToken.None);
        Assert.False(wait.IsCompleted);

        _time.Advance(TimeSpan.FromMinutes(5));

        await wait;
    }
}
