using GreenHedges.HomeServer.Collectors.Libre.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Nightscout;

/// <summary>
/// Publishes archive rows to Nightscout in id order, tracking progress with a watermark in the
/// archive's <c>publish_state</c> table. Every <c>current</c> reading is published; <c>graph</c> and
/// imported <c>libreview</c> readings only when they fill a gap (no higher-priority reading nearby);
/// <c>libreview_scan</c> readings never (sporadic daytime points would skew statistics). Safe to re-run at any point:
/// the watermark only advances after a successful upload, and Nightscout upserts.
/// </summary>
public sealed class NightscoutPublisher(
    GlucoseArchive archive,
    NightscoutClient client,
    IOptions<NightscoutOptions> options,
    ILogger<NightscoutPublisher> logger)
{
    /// <summary>
    /// Watermark name in <c>publish_state</c>. Bump the suffix when the published form of entries changes:
    /// the new name starts from zero, so the whole archive is republished once (Nightscout upserts on
    /// sysTime + type, so existing entries are corrected in place).
    /// v2 (2026-10-17): Libre LO published as sgv 39 (Nightscout LOW), HI as at least 401.
    /// </summary>
    public const string Target = "nightscout-v2";

    private readonly NightscoutOptions _options = options.Value;

    /// <summary>Publishes everything not yet published. Returns the number of entries uploaded.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var uploaded = 0;
        while (true)
        {
            var afterId = await archive.GetPublishedIdAsync(Target, cancellationToken);
            var batch = await archive.ReadAfterAsync(afterId, _options.BatchSize, _options.GraphGapWindow, cancellationToken);
            if (batch.Count == 0)
            {
                return uploaded;
            }

            var entries = batch
                .Where(row => row.Reading.Source switch
                {
                    ReadingSource.Current => true,
                    ReadingSource.Graph or ReadingSource.LibreView => !row.HasNearbyBetter,
                    _ => false,
                })
                .Select(row => NightscoutEntry.From(
                    row.Reading,
                    row.Reading.Source == ReadingSource.LibreView ? $"{_options.Device} (LibreView)" : _options.Device))
                .ToList();

            if (entries.Count > 0)
            {
                await client.UploadAsync(entries, cancellationToken);
            }

            await archive.SetPublishedIdAsync(Target, batch[^1].Id, cancellationToken);
            uploaded += entries.Count;
            logger.LogDebug(
                "Published {Uploaded} of {Rows} rows (ids {First}–{Last}) to Nightscout",
                entries.Count,
                batch.Count,
                batch[0].Id,
                batch[^1].Id);

            if (batch.Count < _options.BatchSize)
            {
                return uploaded;
            }
        }
    }
}
