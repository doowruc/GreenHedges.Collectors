namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Tests;

public sealed class PollScheduleTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Offset = TimeSpan.FromSeconds(35);

    private static DateTimeOffset At(int minute, double second) =>
        new DateTimeOffset(2026, 10, 17, 0, minute, 0, TimeSpan.Zero).AddSeconds(second);

    private static DateTimeOffset Next(DateTimeOffset sensorTime, DateTimeOffset now) =>
        CollectorService.NextAlignedPoll(sensorTime, now, Interval, Offset);

    [Fact]
    public void Steady_state_polls_offset_after_the_next_reading()
    {
        // Polled at 00:26:53 (35 s after a reading stamped 00:26:18): next poll 35 s after the 00:27 reading.
        Assert.Equal(At(27, 53), Next(sensorTime: At(26, 18), now: At(26, 53.1)));
    }

    [Fact]
    public void Misaligned_start_moves_to_the_aligned_phase()
    {
        // The failure case seen in testing: polling at :27, just after readings stamped :18.
        // The next poll moves to :53, not :27.
        Assert.Equal(At(26, 53), Next(sensorTime: At(25, 18), now: At(25, 27)));
    }

    [Fact]
    public void Stale_reading_keeps_the_same_phase()
    {
        // No new reading for a few minutes: keep polling once a minute at :53.
        Assert.Equal(At(30, 53), Next(sensorTime: At(26, 18), now: At(29, 53.2)));
    }

    [Fact]
    public void Never_polls_sooner_than_half_an_interval()
    {
        // Reading only just in (first poll at startup): the aligned time 33 s away is allowed...
        Assert.Equal(At(26, 53), Next(sensorTime: At(26, 18), now: At(26, 20)));

        // ...but one only 10 s away is pushed to the following minute.
        Assert.Equal(At(27, 53), Next(sensorTime: At(26, 18), now: At(26, 43)));
    }

    [Fact]
    public void Follows_sensor_drift()
    {
        Assert.Equal(At(28, 55), Next(sensorTime: At(27, 20), now: At(27, 53.1)));
    }
}
