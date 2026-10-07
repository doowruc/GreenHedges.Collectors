using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;
using VehicleManager.Services;

namespace VehicleManager.Pages.Vehicles;

public class DetailsModel(AppDbContext db, FuelStatsService stats, VehicleLookup lookup) : PageModel
{
    public VehicleView Vehicle { get; private set; } = new();
    public List<FuelEntryView> Entries { get; private set; } = [];
    public FuelSummary AllTime { get; private set; } = FuelSummary.Empty;
    public FuelSummary LastYear { get; private set; } = FuelSummary.Empty;
    public string ChartJson { get; private set; } = "[]";
    public List<DueDateView> DueDates { get; private set; } = [];
    public MileageTrend? Mileage { get; private set; }

    /// <summary>A service due at a mileage whose date can't be estimated yet (too few odometer readings).</summary>
    public int? UnestimatedServiceMileage { get; private set; }

    /// <summary>The DVLA or DVSA is set up, so MOT (and tax, with the DVLA) can be checked.</summary>
    public bool CanLookUp => lookup.IsAvailable;

    /// <summary>What a check fills in: "MOT and tax", or just "MOT".</summary>
    public string LookedUp => lookup.LookedUp;

    /// <summary>All vehicles for the switcher; each option's value is that vehicle's details URL.</summary>
    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleView.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;

        VehicleOptions = (await db.Vehicles.OrderBy(v => v.Make).ThenBy(v => v.Model)
                .Select(v => new { v.Id, v.Make, v.Model, v.RegistrationNumber }).ToListAsync())
            .Select(v => new SelectListItem(
                $"{v.Make} {v.Model} ({v.RegistrationNumber})", Url.Page("Details", new { id = v.Id }), v.Id == id))
            .ToList();

        Entries = await db.FuelEntries
            .Where(f => f.VehicleId == id)
            .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
            .Select(FuelEntryView.Projection)
            .ToListAsync();

        Mileage = MileageTrend.From(Entries);
        DueDates = vehicle.DueDates(Reminders.UkToday(), Mileage);
        if (Mileage is null) UnestimatedServiceMileage = vehicle.ServiceDueMileage;

        AllTime = stats.Summarise(Entries);
        LastYear = stats.Summarise(Entries, DateOnly.FromDateTime(DateTime.Today.AddYears(-1)));

        ChartJson = JsonSerializer.Serialize(
            Entries.AsEnumerable().Reverse().Select(f => new
            {
                date = f.Date.ToString("dd/MM/yy"),
                mpg = f.CalculatedMpg,
                dash = f.TripAverageMpg,
                price = f.EffectiveCostPerLitre
            }));

        return Page();
    }

    /// <summary>Checks the MOT and tax due dates with whichever of the DVLA and DVSA are set up, and saves any found.</summary>
    public async Task<IActionResult> OnPostLookupAsync(int id)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null) return NotFound();
        if (!lookup.IsAvailable) return BadRequest();

        var found = await lookup.RefreshAsync(vehicle);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Checked {vehicle.RegistrationNumber} with {lookup.ServiceNames}: {found.Summary}.";
        if (found.Problems.Count > 0) TempData["Warning"] = string.Join(" ", found.Problems);
        return RedirectToPage(new { id });
    }
}
