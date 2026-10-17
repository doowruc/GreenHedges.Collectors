namespace GreenHedges.Collectors.Weather.Rtl433;

public sealed class Rtl433Options
{
    public const string SectionName = "Rtl433";

    /// <summary>The rtl_433 executable.</summary>
    public string Path { get; set; } = "rtl_433";

    /// <summary>
    /// Arguments, separated by spaces (none of rtl_433's need quoting). The default receives the Bresser
    /// 5-in-1 (protocol 172) at 868.3 MHz, with epoch times, signal levels and protocol numbers, as JSON lines.
    /// </summary>
    public string Arguments { get; set; } = "-f 868.3M -s 1024k -R 172 -M time:unix:usec -M level -M protocol -F json";

    /// <summary>Kill and restart rtl_433 if it writes no line for this long (it can hang after a USB glitch).</summary>
    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Wait before the first restart; doubled for each restart in a row, up to <see cref="MaxRestartDelay"/>.</summary>
    public TimeSpan MinRestartDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRestartDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A run at least this long counts as healthy: the next restart starts again from <see cref="MinRestartDelay"/>.</summary>
    public TimeSpan StableAfter { get; set; } = TimeSpan.FromMinutes(10);

    internal string[] ArgumentList => Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
