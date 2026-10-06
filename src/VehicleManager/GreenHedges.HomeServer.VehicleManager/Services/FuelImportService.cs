using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Services;

public sealed record ParsedSheet(string Name, List<FuelEntry> Entries, List<string> Warnings)
{
    public DateOnly? From => Entries.Count > 0 ? Entries.Min(e => e.Date) : null;
    public DateOnly? To => Entries.Count > 0 ? Entries.Max(e => e.Date) : null;
}

public sealed record ImportResult(int Imported, int SkippedDuplicates);

/// <summary>
/// Imports fill-ups from the historical fuel spreadsheet. Each worksheet is one vehicle;
/// row 2 holds the headers: Date, Brand, Location, Litres, Per Litre, [Voucher], [Total],
/// Range, Miles, MPG, Odometer, then calculated columns (ignored – the app recalculates them).
/// </summary>
public class FuelImportService(AppDbContext db)
{
    public List<ParsedSheet> Parse(Stream xlsx) =>
        XlsxReader.Read(xlsx).Select(ParseSheet).ToList();

    public static ParsedSheet ParseSheet(XlsxReader.Sheet sheet)
    {
        var warnings = new List<string>();
        var entries = new List<FuelEntry>();

        // Find the header row: the first row (within the first 10) that has both "Date" and "Litres".
        var headerIndex = sheet.Rows.Take(10).ToList().FindIndex(r =>
            r.Values.Any(v => v.Equals("Date", StringComparison.OrdinalIgnoreCase)) &&
            r.Values.Any(v => v.Equals("Litres", StringComparison.OrdinalIgnoreCase)));

        if (headerIndex < 0)
        {
            warnings.Add("No header row with 'Date' and 'Litres' found – sheet skipped.");
            return new ParsedSheet(sheet.Name, entries, warnings);
        }

        // Map header name -> column. First occurrence wins, so the trip-computer "Miles"/"MPG"
        // are used rather than the later "Calculated" columns with the same names.
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, name) in sheet.Rows[headerIndex].OrderBy(kv => kv.Key))
            columns.TryAdd(name, col);

        string? Cell(IReadOnlyDictionary<int, string> row, string header) =>
            columns.TryGetValue(header, out var c) && row.TryGetValue(c, out var v) ? v : null;

        foreach (var (row, i) in sheet.Rows.Skip(headerIndex + 1).Select((r, i) => (r, i)))
        {
            var excelRow = headerIndex + 2 + i;
            if (row.Count == 0) continue;

            var date = XlsxReader.ToDate(Cell(row, "Date"));
            var litres = XlsxReader.ToDecimal(Cell(row, "Litres"));
            var price = XlsxReader.ToDecimal(Cell(row, "Per Litre"));

            if (date is null && litres is null) continue; // blank / summary row
            if (date is null || litres is not > 0 || price is not > 0)
            {
                warnings.Add($"Row {excelRow}: missing date, litres or price – skipped.");
                continue;
            }

            var odometer = XlsxReader.ToDecimal(Cell(row, "Odometer"));
            var range = XlsxReader.ToDecimal(Cell(row, "Range"));
            var voucher = XlsxReader.ToDecimal(Cell(row, "Voucher"));

            entries.Add(new FuelEntry
            {
                Date = date.Value,
                Brand = Cell(row, "Brand"),
                Location = Cell(row, "Location"),
                Litres = Math.Round(litres.Value, 3),
                CostPerLitre = Math.Round(price.Value, 3),
                VoucherPerLitre = voucher is > 0 ? Math.Round(voucher.Value, 3) : null,
                Range = range is null ? null : (int)Math.Round(range.Value),
                TripMiles = XlsxReader.ToDecimal(Cell(row, "Miles")) is { } m ? Math.Round(m, 1) : null,
                TripAverageMpg = XlsxReader.ToDecimal(Cell(row, "MPG")) is { } mpg ? Math.Round(mpg, 1) : null,
                Odometer = odometer is null ? null : (int)Math.Round(odometer.Value)
            });
        }

        return new ParsedSheet(sheet.Name, entries, warnings);
    }

    /// <summary>
    /// Adds entries to a vehicle, skipping any that already exist (same date and litres),
    /// so re-importing an updated spreadsheet only adds the new rows.
    /// </summary>
    public async Task<ImportResult> ImportAsync(int vehicleId, IEnumerable<FuelEntry> entries)
    {
        var existing = (await db.FuelEntries
                .Where(f => f.VehicleId == vehicleId)
                .Select(f => new { f.Date, f.Litres })
                .ToListAsync())
            .Select(x => (x.Date, x.Litres))
            .ToHashSet();

        int imported = 0, skipped = 0;
        foreach (var e in entries)
        {
            if (!existing.Add((e.Date, e.Litres))) { skipped++; continue; }
            e.Id = 0;
            e.VehicleId = vehicleId;
            db.FuelEntries.Add(e);
            imported++;
        }
        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped);
    }
}
