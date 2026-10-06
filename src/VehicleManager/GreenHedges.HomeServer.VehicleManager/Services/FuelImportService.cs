using System.Globalization;
using ClosedXML.Excel;
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
    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    public List<ParsedSheet> Parse(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        return workbook.Worksheets.Select(ParseSheet).ToList();
    }

    public static ParsedSheet ParseSheet(IXLWorksheet sheet)
    {
        var warnings = new List<string>();
        var entries = new List<FuelEntry>();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        // Find the header row: the first row (within the first 10) that has both "Date" and "Litres".
        var headerRow = Enumerable.Range(1, Math.Min(10, lastRow))
            .Select(sheet.Row)
            .FirstOrDefault(r =>
            {
                var texts = r.CellsUsed().Select(c => Text(c)).ToList();
                return texts.Contains("Date", StringComparer.OrdinalIgnoreCase) &&
                       texts.Contains("Litres", StringComparer.OrdinalIgnoreCase);
            });

        if (headerRow is null)
        {
            warnings.Add("No header row with 'Date' and 'Litres' found – sheet skipped.");
            return new ParsedSheet(sheet.Name, entries, warnings);
        }

        // Map header name -> column number. First occurrence wins, so the trip-computer "Miles"/"MPG"
        // are used rather than the later "Calculated" columns with the same names.
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            if (Text(cell) is { } name) columns.TryAdd(name, cell.Address.ColumnNumber);

        IXLCell? Cell(IXLRow row, string header) =>
            columns.TryGetValue(header, out var c) ? row.Cell(c) : null;

        for (var r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            if (row.IsEmpty()) continue;

            var date = Date(Cell(row, "Date"));
            var litres = Number(Cell(row, "Litres"));
            var price = Number(Cell(row, "Per Litre"));

            if (date is null && litres is null) continue; // blank / summary row
            if (date is null || litres is not > 0 || price is not > 0)
            {
                warnings.Add($"Row {r}: missing date, litres or price – skipped.");
                continue;
            }

            var odometer = Number(Cell(row, "Odometer"));
            var range = Number(Cell(row, "Range"));
            var voucher = Number(Cell(row, "Voucher"));

            entries.Add(new FuelEntry
            {
                Date = date.Value,
                Brand = Text(Cell(row, "Brand")),
                Location = Text(Cell(row, "Location")),
                Litres = Math.Round(litres.Value, 3),
                CostPerLitre = Math.Round(price.Value, 3),
                VoucherPerLitre = voucher is > 0 ? Math.Round(voucher.Value, 3) : null,
                Range = range is null ? null : (int)Math.Round(range.Value),
                TripMiles = Number(Cell(row, "Miles")) is { } m ? Math.Round(m, 1) : null,
                TripAverageMpg = Number(Cell(row, "MPG")) is { } mpg ? Math.Round(mpg, 1) : null,
                Odometer = odometer is null ? null : (int)Math.Round(odometer.Value)
            });
        }

        return new ParsedSheet(sheet.Name, entries, warnings);
    }

    // --- Cell helpers. CachedValue reads the value Excel last saved for formula cells
    //     rather than recalculating, and is the plain value for everything else. ---

    private static string? Text(IXLCell? cell)
    {
        if (cell is null) return null;
        var v = cell.CachedValue;
        var s = v.Type switch
        {
            XLDataType.Blank or XLDataType.Error => null,
            XLDataType.Text => v.GetText(),
            _ => cell.GetFormattedString()
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static decimal? Number(IXLCell? cell)
    {
        if (cell is null) return null;
        var v = cell.CachedValue;
        if (v.IsNumber) return (decimal)v.GetNumber();
        if (v.IsText && decimal.TryParse(v.GetText().Trim().TrimStart('£'), NumberStyles.Float, UkCulture, out var d))
            return d;
        return null;
    }

    private static DateOnly? Date(IXLCell? cell)
    {
        if (cell is null) return null;
        var v = cell.CachedValue;
        if (v.IsDateTime) return DateOnly.FromDateTime(v.GetDateTime());
        // Unformatted cells hold dates as OLE Automation serial numbers.
        if (v.IsNumber && v.GetNumber() is > 1 and < 2958466 and var serial)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (v.IsText && DateTime.TryParse(v.GetText(), UkCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
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
