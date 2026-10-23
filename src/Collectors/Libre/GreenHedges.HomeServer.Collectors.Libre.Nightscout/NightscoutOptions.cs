namespace GreenHedges.HomeServer.Collectors.Libre.Nightscout;

public sealed class NightscoutOptions
{
    public const string SectionName = "Nightscout";

    /// <summary>Nightscout's address, e.g. <c>http://127.0.0.1:1337</c>. Publishing is off when empty.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Nightscout's <c>API_SECRET</c> (sent as its SHA-1 hash).</summary>
    public string? ApiSecret { get; set; }

    /// <summary>Readings per upload request.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Gap-fill window: a <c>graph</c> or imported <c>libreview</c> reading is only published if no
    /// higher-priority reading lies within this much of it, so overlapping sources don't draw doubled points.
    /// </summary>
    public TimeSpan GraphGapWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Publish at least this often, even without a signal from the collector.</summary>
    public TimeSpan FallbackInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Device name shown in Nightscout for these entries.</summary>
    public string Device { get; set; } = "LibreCollector";

    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);
}
