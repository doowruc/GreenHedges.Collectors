using Microsoft.Extensions.Logging.Abstractions;

namespace GreenHedges.Collectors.Weather.Rtl433.Tests;

/// <summary>Runs a shell script standing in for rtl_433, so these tests need no dongle.</summary>
public sealed class Rtl433RunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rtl433-fake-{Guid.NewGuid():N}");
    private readonly ReceiverHealth _health = new(TimeProvider.System);
    private readonly List<string> _lines = [];

    public Rtl433RunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Rtl433Options FakeRtl433(string script, TimeSpan? silenceTimeout = null)
    {
        var path = Path.Combine(_dir, $"fake-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, script.ReplaceLineEndings("\n"));
        return new Rtl433Options
        {
            Path = "/bin/sh",
            Arguments = path,
            SilenceTimeout = silenceTimeout ?? TimeSpan.FromSeconds(30),
            MinRestartDelay = TimeSpan.FromMilliseconds(10),
            MaxRestartDelay = TimeSpan.FromMilliseconds(50),
        };
    }

    private Rtl433Runner Runner(Rtl433Options options) =>
        new(options, _health, TimeProvider.System, NullLogger<Rtl433Runner>.Instance);

    private Task Collect(string line)
    {
        lock (_lines)
        {
            _lines.Add(line);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Lines_are_handed_over_in_order_until_rtl_433_exits()
    {
        var options = FakeRtl433("""
            echo '{"model" : "Bresser-6in1", "id" : 1}'
            echo 'not json'
            echo 'starting up' >&2
            exit 3
            """);

        var result = await Runner(options).RunOnceAsync(Collect, CancellationToken.None);

        Assert.Equal(RunEnd.Exited, result.End);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(2, result.Lines);
        Assert.False(result.DongleMissing);
        Assert.Equal(["""{"model" : "Bresser-6in1", "id" : 1}""", "not json"], _lines);

        var health = _health.Snapshot();
        Assert.False(health.IsRunning);
        Assert.NotNull(health.LastLineAt);
        Assert.Equal(1, health.EndsInLastHour);
    }

    [Fact]
    public async Task Silent_rtl_433_is_killed()
    {
        var options = FakeRtl433("""
            echo 'one'
            sleep 30
            echo 'never'
            """, silenceTimeout: TimeSpan.FromMilliseconds(500));

        var started = DateTimeOffset.UtcNow;
        var result = await Runner(options).RunOnceAsync(Collect, CancellationToken.None);

        Assert.Equal(RunEnd.Silent, result.End);
        Assert.Equal(["one"], _lines);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Stopping_ends_the_run_without_counting_it_as_a_failure()
    {
        var options = FakeRtl433("""
            echo 'one'
            sleep 30
            """);
        using var stop = new CancellationTokenSource();

        var result = await Runner(options).RunOnceAsync(
            async line =>
            {
                await Collect(line);
                await stop.CancelAsync();
            },
            stop.Token);

        Assert.Equal(RunEnd.Stopped, result.End);
        Assert.Equal(0, _health.Snapshot().EndsInLastHour);
    }

    [Fact]
    public async Task Missing_dongle_is_reported_and_cleared_by_the_next_line()
    {
        var missing = FakeRtl433("""
            echo 'Found 0 device(s)' >&2
            echo 'No supported devices found.' >&2
            exit 1
            """);

        var result = await Runner(missing).RunOnceAsync(Collect, CancellationToken.None);

        Assert.True(result.DongleMissing);
        Assert.NotNull(_health.Snapshot().DongleMissingSince);

        await Runner(FakeRtl433("echo '{}'")).RunOnceAsync(Collect, CancellationToken.None);
        Assert.Null(_health.Snapshot().DongleMissingSince);
    }

    [Fact]
    public async Task Missing_executable_is_a_failed_start()
    {
        var options = new Rtl433Options { Path = Path.Combine(_dir, "no-such-rtl_433") };

        var result = await Runner(options).RunOnceAsync(Collect, CancellationToken.None);

        Assert.Equal(RunEnd.StartFailed, result.End);
        Assert.NotNull(result.Error);
        Assert.Equal(1, _health.Snapshot().EndsInLastHour);
    }

    [Fact]
    public async Task A_failing_line_handler_does_not_stop_reading()
    {
        var options = FakeRtl433("""
            echo 'first'
            echo 'second'
            """);

        var result = await Runner(options).RunOnceAsync(
            line => line == "first" ? throw new IOException("database locked") : Collect(line),
            CancellationToken.None);

        Assert.Equal(2, result.Lines);
        Assert.Equal(["second"], _lines);
    }

    [Fact]
    public async Task Supervisor_restarts_rtl_433_after_it_exits()
    {
        var options = FakeRtl433("""
            echo 'line'
            exit 1
            """);
        var supervisor = new Rtl433Supervisor(Runner(options), options, TimeProvider.System, NullLogger<Rtl433Supervisor>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await supervisor.RunAsync(
            async line =>
            {
                await Collect(line);
                if (_lines.Count == 3)
                {
                    await stop.CancelAsync();
                }
            },
            stop.Token);

        Assert.Equal(3, _lines.Count);
        Assert.Equal(2, _health.Snapshot().EndsInLastHour);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(6, 160)]
    [InlineData(7, 300)]
    [InlineData(100, 300)]
    public void Restart_delay_doubles_up_to_the_maximum(int endsInARow, int expectedSeconds)
    {
        var delay = Rtl433Supervisor.RestartDelay(endsInARow, new Rtl433Options());

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Default_arguments_receive_the_bresser_at_868_mhz_as_json()
    {
        Assert.Equal(
            ["-f", "868.3M", "-s", "1024k", "-R", "172", "-M", "time:unix:usec", "-M", "level", "-M", "protocol", "-F", "json"],
            new Rtl433Options().ArgumentList);
    }
}
