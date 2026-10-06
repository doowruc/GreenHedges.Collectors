using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace VehicleManager.Services;

/// <summary>
/// Minimal, dependency-free reader for .xlsx workbooks. Returns each worksheet as a
/// grid of raw cell strings (numbers in invariant culture, shared strings resolved,
/// formula cells read from their cached values).
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public sealed record Sheet(string Name, IReadOnlyList<IReadOnlyDictionary<int, string>> Rows);

    public static List<Sheet> Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(zip);

        var workbook = LoadXml(zip, "xl/workbook.xml")
            ?? throw new InvalidDataException("Not a valid .xlsx file (xl/workbook.xml missing).");
        var rels = LoadXml(zip, "xl/_rels/workbook.xml.rels");

        var targets = rels?.Root?.Elements(PkgRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!)
            ?? [];

        var sheets = new List<Sheet>();
        foreach (var s in workbook.Root!.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
        {
            var name = (string?)s.Attribute("name") ?? "Sheet";
            var relId = (string?)s.Attribute(RelNs + "id");
            if (relId is null || !targets.TryGetValue(relId, out var target)) continue;

            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            var sheetXml = LoadXml(zip, path);
            if (sheetXml is null) continue;

            sheets.Add(new Sheet(name, ReadRows(sheetXml, sharedStrings)));
        }
        return sheets;
    }

    private static List<IReadOnlyDictionary<int, string>> ReadRows(XDocument sheetXml, List<string> sharedStrings)
    {
        var rows = new List<IReadOnlyDictionary<int, string>>();
        var sheetData = sheetXml.Root!.Element(Main + "sheetData");
        if (sheetData is null) return rows;

        foreach (var row in sheetData.Elements(Main + "row"))
        {
            // Respect explicit row numbers so blank rows keep their position.
            if (int.TryParse((string?)row.Attribute("r"), out var rowNum))
                while (rows.Count < rowNum - 1) rows.Add(new Dictionary<int, string>());

            var cells = new Dictionary<int, string>();
            var nextCol = 0;
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r")) ?? nextCol;
                nextCol = col + 1;

                var type = (string?)c.Attribute("t");
                string? value = type switch
                {
                    "s" => int.TryParse(c.Element(Main + "v")?.Value, out var i) && i < sharedStrings.Count
                        ? sharedStrings[i] : null,
                    "inlineStr" => string.Concat(c.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? []),
                    _ => c.Element(Main + "v")?.Value
                };
                if (!string.IsNullOrWhiteSpace(value)) cells[col] = value.Trim();
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var doc = LoadXml(zip, "xl/sharedStrings.xml");
        if (doc is null) return [];
        return doc.Root!.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static XDocument? LoadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    /// <summary>"A1" → 0, "AB12" → 27.</summary>
    private static int? ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return null;
        var col = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return col == 0 ? null : col - 1;
    }

    // --- Value helpers ---

    public static decimal? ToDecimal(string? s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static DateOnly? ToDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Excel stores dates as OLE Automation serial numbers.
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is > 1 and < 2958466)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }
}
