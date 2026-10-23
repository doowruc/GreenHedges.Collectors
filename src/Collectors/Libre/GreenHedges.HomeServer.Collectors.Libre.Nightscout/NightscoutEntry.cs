using System.Globalization;
using GreenHedges.HomeServer.Collectors.Libre.Storage;

namespace GreenHedges.HomeServer.Collectors.Libre.Nightscout;

/// <summary>A Nightscout <c>sgv</c> entry as posted to <c>/api/v1/entries</c> (serialised camelCase).</summary>
/// <param name="Sgv">Glucose in mg/dL. Nightscout always stores mg/dL, whatever its display units.</param>
/// <param name="Date">Sensor time, Unix epoch milliseconds.</param>
/// <param name="DateString">Sensor time, ISO 8601 UTC. Nightscout derives <c>sysTime</c> (its dedup key) from this.</param>
public sealed record NightscoutEntry(string Type, int Sgv, long Date, string DateString, string Direction, string Device)
{
    /// <summary>
    /// LibreLinkUp TrendArrow to Nightscout direction, matching nightscout-connect's LibreLinkUp source.
    /// Graph readings carry no trend, so they map to <c>NOT COMPUTABLE</c>.
    /// </summary>
    public static string DirectionFor(int? trendArrow) => trendArrow switch
    {
        1 => "SingleDown",
        2 => "FortyFiveDown",
        3 => "Flat",
        4 => "FortyFiveUp",
        5 => "SingleUp",
        _ => "NOT COMPUTABLE",
    };

    /// <summary>Nightscout shows sgv 39 as "LOW" (below 39 are error codes).</summary>
    public const int NightscoutLow = 39;

    /// <summary>Nightscout shows any sgv above 400 as "HIGH".</summary>
    public const int NightscoutHighThreshold = 400;

    /// <summary>
    /// The sgv to publish. Libre "LO" readings (<see cref="ArchivedReading.IsLow"/>, reported at the 40 mg/dL
    /// floor; the true value is lower) become Nightscout's LOW (39). Libre "HI" readings become at least 401 so
    /// Nightscout shows HIGH (Libre reports HI at 500, so this is a safeguard). Everything else is the value rounded.
    /// </summary>
    public static int SgvFor(ArchivedReading reading)
    {
        var sgv = (int)Math.Round(reading.ValueMgPerDl, MidpointRounding.AwayFromZero);
        if (reading.IsLow)
        {
            return NightscoutLow;
        }

        return reading.IsHigh ? Math.Max(sgv, NightscoutHighThreshold + 1) : sgv;
    }

    public static NightscoutEntry From(ArchivedReading reading, string device) =>
        new(
            "sgv",
            SgvFor(reading),
            reading.TimestampUtc.ToUnixTimeMilliseconds(),
            reading.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            DirectionFor(reading.TrendArrow),
            device);
}
