using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Pages.Import;

/// <summary>
/// Two-step import: (1) upload the workbook and preview each sheet, (2) map each sheet to a
/// vehicle and import. The uploaded file is held in the temp folder between the two steps.
/// </summary>
public partial class IndexModel(AppDbContext db, FuelImportService importer, ILogger<IndexModel> logger) : PageModel
{
    private const long MaxBytes = 10 * 1024 * 1024;

    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? Token { get; set; }
    [BindProperty] public List<int?> VehicleForSheet { get; set; } = [];

    public List<ParsedSheet> Sheets { get; private set; } = [];
    public List<Vehicle> Vehicles { get; private set; } = [];

    public async Task OnGetAsync() => Vehicles = await LoadVehiclesAsync();

    public async Task<IActionResult> OnPostUploadAsync()
    {
        Vehicles = await LoadVehiclesAsync();

        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Choose an .xlsx file to upload.");
            return Page();
        }
        if (Upload.Length > MaxBytes || !Upload.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Upload), "Upload an Excel .xlsx file under 10 MB.");
            return Page();
        }

        var token = Guid.NewGuid().ToString("N");
        await using (var fs = System.IO.File.Create(TempPath(token)))
            await Upload.CopyToAsync(fs);

        if (!TryParse(token, out var sheets)) return Page();

        Token = token;
        Sheets = sheets;
        VehicleForSheet = sheets.Select(s => GuessVehicle(s.Name, Vehicles)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        Vehicles = await LoadVehiclesAsync();
        if (Token is null || !Guid.TryParseExact(Token, "N", out _) || !System.IO.File.Exists(TempPath(Token)))
        {
            ModelState.AddModelError(string.Empty, "The uploaded file has expired – please upload it again.");
            return Page();
        }
        if (!TryParse(Token, out var sheets)) return Page();

        var summary = new List<string>();
        for (var i = 0; i < sheets.Count; i++)
        {
            var vehicleId = i < VehicleForSheet.Count ? VehicleForSheet[i] : null;
            var vehicle = Vehicles.FirstOrDefault(v => v.Id == vehicleId);
            if (vehicle is null || sheets[i].Entries.Count == 0) continue;

            var result = await importer.ImportAsync(vehicle.Id, sheets[i].Entries);
            summary.Add($"{sheets[i].Name} → {vehicle.DisplayName}: {result.Imported} added" +
                        (result.SkippedDuplicates > 0 ? $", {result.SkippedDuplicates} already present" : ""));
        }

        System.IO.File.Delete(TempPath(Token));

        TempData["Message"] = summary.Count > 0
            ? "Import complete. " + string.Join("; ", summary) + "."
            : "Nothing was imported – no sheets were mapped to a vehicle.";
        return RedirectToPage("/Index");
    }

    private bool TryParse(string token, out List<ParsedSheet> sheets)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(TempPath(token));
            sheets = importer.Parse(fs);
            return true;
        }
        // ClosedXML/OpenXML surface a variety of exception types for corrupt or non-xlsx files.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Failed to read uploaded workbook");
            ModelState.AddModelError(string.Empty, "Couldn't read that file – is it a valid .xlsx workbook?");
            sheets = [];
            return false;
        }
    }

    private Task<List<Vehicle>> LoadVehiclesAsync() =>
        db.Vehicles.OrderBy(v => v.Make).ThenBy(v => v.Model).ToListAsync();

    public List<SelectListItem> OptionsFor(int index)
    {
        var selected = index < VehicleForSheet.Count ? VehicleForSheet[index] : null;
        return [
            new SelectListItem("— skip this sheet —", "", selected is null),
            .. Vehicles.Select(v => new SelectListItem(
                $"{v.DisplayName} ({v.RegistrationNumber})", v.Id.ToString(), v.Id == selected))
        ];
    }

    private static string TempPath(string token) =>
        Path.Combine(Path.GetTempPath(), $"vehiclemanager-import-{token}.xlsx");

    /// <summary>Pick the vehicle whose make/model best matches the sheet name (e.g. "Yaris", "A Class").</summary>
    private static int? GuessVehicle(string sheetName, List<Vehicle> vehicles)
    {
        var sheet = Normalise(sheetName);
        if (sheet.Length == 0) return null;

        var scored = vehicles.Select(v =>
        {
            var make = Normalise(v.Make);
            var model = Normalise(v.Model);
            var score =
                sheet == model || sheet == make + model || sheet == Normalise(v.RegistrationNumber) ? 3 :
                model.Length > 0 && (model.Contains(sheet) || sheet.Contains(model)) ? 2 :
                make.Length > 0 && (make.Contains(sheet) || sheet.Contains(make)) ? 1 : 0;
            return (v.Id, score);
        }).Where(x => x.score > 0).OrderByDescending(x => x.score).ToList();

        if (scored.Count == 0) return null;
        // Ambiguous (e.g. two Mercedes and a sheet called "Mercedes") – let the user choose.
        if (scored.Count > 1 && scored[0].score == scored[1].score) return null;
        return scored[0].Id;
    }

    private static string Normalise(string? s) => NonAlphaNum().Replace(s ?? "", "").ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphaNum();
}
