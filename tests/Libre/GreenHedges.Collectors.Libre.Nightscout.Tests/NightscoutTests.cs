using System.Net;
using System.Text.Json;
using GreenHedges.Collectors.Libre.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Libre.Nightscout.Tests;

public sealed class NightscoutTests : IDisposable
{
    private const string Secret = "test-api-secret-123";

    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 2, 26, 18, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"librecollector-ns-test-{Guid.NewGuid():N}.db");
    private readonly GlucoseArchive _archive;
    private readonly RecordingHandler _handler = new();

    public NightscoutTests() => _archive = new GlucoseArchive(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static NightscoutOptions Options(int batchSize = 500) => new()
    {
        BaseUrl = "http://127.0.0.1:1337",
        ApiSecret = Secret,
        BatchSize = batchSize,
    };

    private NightscoutPublisher Publisher(int batchSize = 500)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options(batchSize));
        return new NightscoutPublisher(
            _archive,
            new NightscoutClient(new HttpClient(_handler), options),
            options,
            NullLogger<NightscoutPublisher>.Instance);
    }

    private static ArchivedReading Reading(DateTimeOffset timestamp, ReadingSource source, double value = 115, int? trend = 3) =>
        new(timestamp, source, null, value, trend, 1, false, false, timestamp.AddSeconds(35), "{}");

    [Theory]
    [InlineData(1, "SingleDown")]
    [InlineData(2, "FortyFiveDown")]
    [InlineData(3, "Flat")]
    [InlineData(4, "FortyFiveUp")]
    [InlineData(5, "SingleUp")]
    [InlineData(null, "NOT COMPUTABLE")]
    [InlineData(0, "NOT COMPUTABLE")]
    public void Trend_arrow_maps_like_nightscout_connect(int? trend, string direction) =>
        Assert.Equal(direction, NightscoutEntry.DirectionFor(trend));

    [Theory]
    [InlineData(40, true, false, 39)]    // Libre LO (reported at the floor): Nightscout LOW
    [InlineData(40, false, false, 40)]   // a real 40 without the LO flag stays 40
    [InlineData(39.6, true, false, 39)]  // imported LibreView floor (2.2 mmol/L x 18)
    [InlineData(500, false, true, 500)]  // Libre HI at its 500 ceiling: already above 400, shows HIGH
    [InlineData(380, false, true, 401)]  // any HI flag is published above 400 so Nightscout shows HIGH
    [InlineData(450, false, false, 450)] // genuine high value: unchanged (Nightscout still displays HIGH above 400)
    [InlineData(115.5, false, false, 116)]
    public void Libre_lo_and_hi_map_to_nightscout_low_and_high(double mgdl, bool isLow, bool isHigh, int expected) =>
        Assert.Equal(expected, NightscoutEntry.SgvFor(Reading(T0, ReadingSource.Current, value: mgdl) with { IsLow = isLow, IsHigh = isHigh }));

    [Fact]
    public async Task Lo_reading_is_published_as_39()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync([Reading(T0, ReadingSource.Current, value: 40, trend: 0) with { IsLow = true }], CancellationToken.None);

        await Publisher().PublishPendingAsync(CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body);
        var entry = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(39, entry.GetProperty("sgv").GetInt32());
        Assert.Equal("NOT COMPUTABLE", entry.GetProperty("direction").GetString());
    }

    [Fact]
    public void Entry_uses_sensor_time_and_whole_mg_per_dl()
    {
        var entry = NightscoutEntry.From(Reading(T0, ReadingSource.Current, value: 115.5, trend: 4), "LibreCollector");

        Assert.Equal("sgv", entry.Type);
        Assert.Equal(116, entry.Sgv);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), entry.Date);
        Assert.Equal("2026-10-17T02:26:18.000Z", entry.DateString);
        Assert.Equal("FortyFiveUp", entry.Direction);
        Assert.Equal("LibreCollector", entry.Device);
    }

    [Fact]
    public async Task Client_posts_camel_case_entries_with_hashed_secret()
    {
        var settings = Options();
        settings.BaseUrl = "http://127.0.0.1:1337/"; // trailing slash tolerated
        var client = new NightscoutClient(new HttpClient(_handler), Microsoft.Extensions.Options.Options.Create(settings));

        await client.UploadAsync([NightscoutEntry.From(Reading(T0, ReadingSource.Current), "LibreCollector")], CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("http://127.0.0.1:1337/api/v1/entries", request.Uri);
        // printf '%s' 'test-api-secret-123' | shasum -a 1
        Assert.Equal("086ea68e9c4d37710f77552a4617e114f0ceb22a", request.ApiSecret);

        using var body = JsonDocument.Parse(request.Body);
        var entry = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("sgv", entry.GetProperty("type").GetString());
        Assert.Equal(115, entry.GetProperty("sgv").GetInt32());
        Assert.Equal(T0.ToUnixTimeMilliseconds(), entry.GetProperty("date").GetInt64());
        Assert.Equal("2026-10-17T02:26:18.000Z", entry.GetProperty("dateString").GetString());
        Assert.Equal("Flat", entry.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task Client_throws_on_error_response()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        var client = new NightscoutClient(new HttpClient(_handler), Microsoft.Extensions.Options.Options.Create(Options()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.UploadAsync([NightscoutEntry.From(Reading(T0, ReadingSource.Current), "x")], CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Publishes_current_readings_and_gap_filling_graph_points_only()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [
                Reading(T0, ReadingSource.Current),
                Reading(T0.AddMinutes(1), ReadingSource.Current),
                Reading(T0.AddSeconds(1), ReadingSource.Graph, value: 112, trend: null),   // duplicate of a current point
                Reading(T0.AddMinutes(-30), ReadingSource.Graph, value: 100, trend: null), // fills a gap
            ],
            CancellationToken.None);

        var uploaded = await Publisher().PublishPendingAsync(CancellationToken.None);

        Assert.Equal(3, uploaded);
        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body);
        Assert.Equal([115, 115, 100], body.RootElement.EnumerateArray().Select(e => e.GetProperty("sgv").GetInt32()));
        Assert.Equal(4, await _archive.GetPublishedIdAsync(NightscoutPublisher.Target, CancellationToken.None));

        // Nothing new: no request at all.
        Assert.Equal(0, await Publisher().PublishPendingAsync(CancellationToken.None));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Imported_history_fills_gaps_only_and_scans_are_never_published()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [
                Reading(T0, ReadingSource.Current),
                Reading(T0.AddSeconds(-18), ReadingSource.LibreView, value: 114, trend: null),           // duplicate of current
                Reading(T0.AddDays(-400), ReadingSource.LibreView, value: 101, trend: null),             // history
                Reading(T0.AddDays(-400).AddMinutes(7), ReadingSource.LibreViewScan, value: 99, trend: null),
            ],
            CancellationToken.None);

        Assert.Equal(2, await Publisher().PublishPendingAsync(CancellationToken.None));

        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body);
        var entries = body.RootElement.EnumerateArray().ToList();
        Assert.Equal([115, 101], entries.Select(e => e.GetProperty("sgv").GetInt32()));
        Assert.Equal("LibreCollector (LibreView)", entries[1].GetProperty("device").GetString());
        Assert.Equal("NOT COMPUTABLE", entries[1].GetProperty("direction").GetString());
    }

    [Fact]
    public async Task Failed_upload_keeps_the_watermark_so_it_is_retried()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync([Reading(T0, ReadingSource.Current)], CancellationToken.None);
        _handler.Status = HttpStatusCode.ServiceUnavailable;

        await Assert.ThrowsAsync<HttpRequestException>(() => Publisher().PublishPendingAsync(CancellationToken.None));
        Assert.Equal(0, await _archive.GetPublishedIdAsync(NightscoutPublisher.Target, CancellationToken.None));

        _handler.Status = HttpStatusCode.OK;
        Assert.Equal(1, await Publisher().PublishPendingAsync(CancellationToken.None));
        Assert.Equal(1, await _archive.GetPublishedIdAsync(NightscoutPublisher.Target, CancellationToken.None));
    }

    [Fact]
    public async Task Large_backlog_is_sent_in_batches()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [.. Enumerable.Range(0, 5).Select(i => Reading(T0.AddMinutes(i), ReadingSource.Current))],
            CancellationToken.None);

        var uploaded = await Publisher(batchSize: 2).PublishPendingAsync(CancellationToken.None);

        Assert.Equal(5, uploaded);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal(5, await _archive.GetPublishedIdAsync(NightscoutPublisher.Target, CancellationToken.None));
    }

    private sealed record RecordedRequest(string Uri, string? ApiSecret, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("api-secret", out var values) ? values.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status) { Content = new StringContent("[]") };
        }
    }
}
