using System.Globalization;
using System.Text;
using System.Text.Json;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace GreenHedges.HomeServer.Collectors.Libre.LibreView;

/// <summary>Counts from one import. Re-running the same file reports everything as already present.</summary>
public sealed record LibreViewImportResult
{
    public int RowsRead { get; set; }
    public int ReadingsImported { get; set; }
    public int ReadingsAlreadyPresent { get; set; }
    public int ScansImported { get; set; }
    public int ScansAlreadyPresent { get; set; }
    public int EventsImported { get; set; }
    public int EventsAlreadyPresent { get; set; }
    public int SkippedAmbiguousTime { get; set; }
    public int SkippedInvalidTime { get; set; }
    public int SkippedEmpty { get; set; }
    public int SkippedUnparseable { get; set; }
    public DateTimeOffset? FirstUtc { get; set; }
    public DateTimeOffset? LastUtc { get; set; }
}

/// <summary>
/// Imports a LibreView "Download glucose data" CSV into the archive (see docs/DESIGN.md, LibreView export import).
/// Historic glucose becomes source <c>libreview</c>, scans <c>libreview_scan</c>; insulin, carbs, notes and
/// other records become events. Local timestamps are converted with <paramref name="zone"/>; times that are
/// ambiguous (autumn clock change) or invalid (spring) are skipped, since they can't be placed reliably.
/// Idempotent: re-importing adds only rows not already archived.
/// </summary>
public sealed class LibreViewImporter(GlucoseArchive archive, TimeProvider time, ILogger<LibreViewImporter> logger)
{
    public const string DefaultTimestampFormat = "dd-MM-yyyy HH:mm";
    private const string Source = "libreview";
    private const int BatchSize = 5000;
    private const double MgPerDlPerMmol = 18.0;
    private const double LoFloorMmol = 2.2;
    private const double HiCeilingMmol = 27.8;

    // Non-glucose columns → event kind and unit (unit null = free text). Matched by header prefix,
    // since LibreView varies some names slightly.
    private static readonly (string HeaderPrefix, string Kind, string? Unit)[] EventColumns =
    [
        ("Rapid-Acting Insulin (units)", "rapid_acting_insulin", "units"),
        ("Non-numeric Rapid-Acting Insulin", "rapid_acting_insulin", null),
        ("Long-Acting Insulin", "long_acting_insulin", "units"),
        ("Non-numeric Long-Acting Insulin", "long_acting_insulin", null),
        ("Meal Insulin (units)", "meal_insulin", "units"),
        ("Correction Insulin (units)", "correction_insulin", "units"),
        ("User Change Insulin (units)", "user_change_insulin", "units"),
        ("Carbohydrates (grams)", "carbs", "g"),
        ("Carbohydrates (servings)", "carbs_servings", "servings"),
        ("Non-numeric Food", "food", null),
        ("Notes", "note", null),
        ("Strip Glucose", "strip_glucose", "*"),
        ("Ketone", "ketone", "mmol/L"),
        ("Historic Ketone", "ketone", "mmol/L"),
        ("Scan Ketone", "ketone", "mmol/L"),
    ];

    public async Task<LibreViewImportResult> ImportAsync(
        string path,
        TimeZoneInfo zone,
        string timestampFormat = DefaultTimestampFormat,
        CancellationToken cancellationToken = default)
    {
        var result = new LibreViewImportResult();
        var receivedAt = time.GetUtcNow();
        var readings = new List<ArchivedReading>();
        var events = new List<ArchivedEvent>();

        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true,
        };
        parser.SetDelimiters(",");

        // Line 1 is a title/metadata row (it includes the account name, so it's never logged).
        parser.ReadLine();
        var headers = parser.ReadFields() ?? throw new InvalidDataException("The file has no header row.");
        var columns = Columns.From(headers);

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }

            result.RowsRead++;
            var localText = Field(fields, columns.Timestamp);
            if (!DateTime.TryParseExact(localText, timestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                result.SkippedUnparseable++;
                continue;
            }

            if (zone.IsInvalidTime(local))
            {
                result.SkippedInvalidTime++;
                continue;
            }

            if (zone.IsAmbiguousTime(local))
            {
                result.SkippedAmbiguousTime++;
                continue;
            }

            var utc = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
            var raw = RawJson(headers, fields);
            var produced = false;

            var recordType = Field(fields, columns.RecordType);
            var glucose = recordType switch
            {
                "0" => (Column: columns.Historic, Source: ReadingSource.LibreView),
                "1" => (Column: columns.Scan, Source: ReadingSource.LibreViewScan),
                _ => (Column: -1, Source: ReadingSource.LibreView),
            };

            if (glucose.Column >= 0 && TryNumber(Field(fields, glucose.Column), out var value))
            {
                var mmol = columns.GlucoseIsMmol ? value : value / MgPerDlPerMmol;
                readings.Add(new ArchivedReading(
                    utc,
                    glucose.Source,
                    localText,
                    columns.GlucoseIsMmol ? value * MgPerDlPerMmol : value,
                    TrendArrow: null,
                    MeasurementColor: null,
                    IsHigh: mmol >= HiCeilingMmol,
                    IsLow: mmol <= LoFloorMmol,
                    receivedAt,
                    raw));
                produced = true;
            }

            foreach (var (index, kind, unit) in columns.Events)
            {
                var text = Field(fields, index);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var number = 0d;
                var numeric = unit is not null && TryNumber(text, out number);
                events.Add(new ArchivedEvent(
                    utc,
                    localText,
                    kind,
                    numeric ? number : null,
                    numeric ? (unit == "*" ? columns.GlucoseUnit : unit) : null,
                    numeric ? null : text,
                    Source,
                    receivedAt,
                    raw));
                produced = true;
            }

            if (!produced)
            {
                result.SkippedEmpty++;
                continue;
            }

            result.FirstUtc = result.FirstUtc is { } first && first < utc ? first : utc;
            result.LastUtc = result.LastUtc is { } last && last > utc ? last : utc;

            if (readings.Count >= BatchSize)
            {
                await FlushReadingsAsync(readings, result, cancellationToken);
            }

            if (events.Count >= BatchSize)
            {
                await FlushEventsAsync(events, result, cancellationToken);
            }
        }

        await FlushReadingsAsync(readings, result, cancellationToken);
        await FlushEventsAsync(events, result, cancellationToken);

        logger.LogInformation("LibreView import finished: {@Result}", result);
        return result;
    }

    private async Task FlushReadingsAsync(List<ArchivedReading> readings, LibreViewImportResult result, CancellationToken cancellationToken)
    {
        foreach (var group in readings.GroupBy(r => r.Source))
        {
            var batch = group.ToList();
            var inserted = await archive.InsertAsync(batch, cancellationToken);
            if (group.Key == ReadingSource.LibreViewScan)
            {
                result.ScansImported += inserted;
                result.ScansAlreadyPresent += batch.Count - inserted;
            }
            else
            {
                result.ReadingsImported += inserted;
                result.ReadingsAlreadyPresent += batch.Count - inserted;
            }
        }

        readings.Clear();
    }

    private async Task FlushEventsAsync(List<ArchivedEvent> events, LibreViewImportResult result, CancellationToken cancellationToken)
    {
        var inserted = await archive.InsertEventsAsync(events, cancellationToken);
        result.EventsImported += inserted;
        result.EventsAlreadyPresent += events.Count - inserted;
        events.Clear();
    }

    private static string Field(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : "";

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>The CSV row as a header → value object, non-empty fields only, in file order.</summary>
    private static string RawJson(string[] headers, string[] fields)
    {
        var row = new Dictionary<string, string>();
        for (var i = 0; i < headers.Length && i < fields.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(fields[i]))
            {
                row[headers[i]] = fields[i].Trim();
            }
        }

        return JsonSerializer.Serialize(row);
    }

    private sealed record Columns(
        int Timestamp,
        int RecordType,
        int Historic,
        int Scan,
        bool GlucoseIsMmol,
        string GlucoseUnit,
        IReadOnlyList<(int Index, string Kind, string? Unit)> Events)
    {
        public static Columns From(string[] headers)
        {
            int Find(string name) => Array.FindIndex(headers, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            int Prefix(string prefix) => Array.FindIndex(headers, h => h.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            var timestamp = Find("Device Timestamp");
            var recordType = Find("Record Type");
            var historic = Prefix("Historic Glucose");
            var scan = Prefix("Scan Glucose");
            if (timestamp < 0 || recordType < 0 || historic < 0 || scan < 0)
            {
                throw new InvalidDataException(
                    "This doesn't look like a LibreView glucose export: expected 'Device Timestamp', 'Record Type', 'Historic Glucose …' and 'Scan Glucose …' columns.");
            }

            var isMmol = headers[historic].Contains("mmol", StringComparison.OrdinalIgnoreCase);

            // Exact-name event columns first, so e.g. "Historic Ketone" isn't also claimed by "Ketone".
            var claimed = new HashSet<int> { timestamp, recordType, historic, scan };
            var events = new List<(int, string, string?)>();
            foreach (var (prefix, kind, unit) in EventColumns.OrderByDescending(c => c.HeaderPrefix.Length))
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    if (!claimed.Contains(i) && headers[i].Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        claimed.Add(i);
                        events.Add((i, kind, unit));
                    }
                }
            }

            return new Columns(timestamp, recordType, historic, scan, isMmol, isMmol ? "mmol/L" : "mg/dL", events);
        }
    }
}
