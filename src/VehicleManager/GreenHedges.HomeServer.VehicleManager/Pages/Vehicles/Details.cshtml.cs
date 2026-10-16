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
    public List<ServiceRecordView> Services { get; private set; } = [];
    public List<MotTestView> MotTests { get; private set; } = [];
    /// <summary>The MOT tests of the last 12 months, for the period toggle.</summary>
    public List<MotTestView> MotTestsLastYear { get; private set; } = [];

    /// <summary>A service due at a mileage whose date can't be estimated yet (too few odometer readings).</summary>
    public int? UnestimatedServiceMileage { get; private set; }

    /// <summary>The DVLA or DVSA is set up, so MOT (and tax, with the DVLA) can be checked.</summary>
    public bool CanLookUp => lookup.IsAvailable;

    /// <summary>
    /// The header offers each check only while its date is missing: the vehicle hasn't been checked yet, or nothing was
    /// found. Otherwise the MOT is checked from the MOT tab. Either service has MOT dates; only the DVLA has tax.
    /// </summary>
    public bool ShowHeaderMotCheck => CanLookUp && Vehicle.MotDueDate is null;
    public bool ShowHeaderTaxCheck => lookup.HasTax && Vehicle.TaxDueDate is null;

    /// <summary>The <c>check</c> values a lookup takes: just the MOT or just the tax (neither means both).</summary>
    public const string CheckMot = "mot", CheckTax = "tax";

    /// <summary>The DVSA is set up, so checks bring in the MOT history.</summary>
    public bool HasMotHistory => lookup.HasMotHistory;

    /// <summary>The MOT tab shows with the DVSA set up, or MOT tests stored from when it was.</summary>
    public bool ShowMotTab => HasMotHistory || MotTests.Count > 0;

    /// <summary>All vehicles for the switcher; each option's value is that vehicle's details URL, on the current tab.</summary>
    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    /// <summary>The tab shown first: fuel (the default), services or MOT, from <c>?tab=</c>.</summary>
    public string Tab { get; private set; } = FuelTab;
    public const string FuelTab = "fuel", ServicesTab = "services", MotTab = "mot";

    public async Task<IActionResult> OnGetAsync(int id, string? tab)
    {
        Tab = tab is ServicesTab or MotTab ? tab : FuelTab;
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleView.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;

        Entries = await db.FuelEntries
            .Where(f => f.VehicleId == id)
            .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
            .Select(FuelEntryView.Projection)
            .ToListAsync();

        Services = await db.ServiceRecords
            .Where(s => s.VehicleId == id)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
            .Select(ServiceRecordView.Projection)
            .ToListAsync();
        ServiceSchedule.SetDue(Services, vehicle.ServiceIntervalMonths, vehicle.ServiceIntervalMiles);

        MotTests = await db.MotTests
            .Where(t => t.VehicleId == id)
            .OrderByDescending(t => t.CompletedDate).ThenByDescending(t => t.Id)
            .Select(MotTestView.Projection)
            .ToListAsync();
        var yearAgo = Reminders.UkToday().AddYears(-1);
        MotTestsLastYear = MotTests.Where(t => t.Date >= yearAgo).ToList();
        if (Tab == MotTab && !ShowMotTab) Tab = FuelTab;

        // Switching vehicle keeps the tab (the script below updates these links when the tab changes).
        VehicleOptions = (await db.Vehicles.OrderBy(v => v.Make).ThenBy(v => v.Model)
                .Select(v => new { v.Id, v.Make, v.Model, v.RegistrationNumber }).ToListAsync())
            .Select(v => new SelectListItem(
                $"{v.Make} {v.Model} ({v.RegistrationNumber})", Url.Page("Details", new { id = v.Id, tab = Tab == FuelTab ? null : Tab }), v.Id == id))
            .ToList();

        Mileage = MileageTrend.From(Entries, MotTests.Where(t => t.Miles is not null).Select(t => (t.Date, t.Miles!.Value)));
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

    /// <summary>
    /// Checks the MOT due date (and MOT history), the tax due date, or both (<paramref name="check"/>: mot, tax or
    /// neither) with whichever of the DVLA and DVSA are set up, and saves any found. Returns to the tab it was asked from.
    /// </summary>
    public async Task<IActionResult> OnPostLookupAsync(int id, string? tab, string? check)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null) return NotFound();
        var parts = check switch { CheckMot => LookupParts.Mot, CheckTax => LookupParts.Tax, _ => LookupParts.All };
        if (!lookup.IsAvailable || (parts == LookupParts.Tax && !lookup.HasTax)) return BadRequest();

        var found = await lookup.RefreshAsync(vehicle, parts);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Checked {vehicle.RegistrationNumber} with {found.CheckedWith}: {found.Summary}.";
        if (found.Problems.Count > 0) TempData["Warning"] = string.Join(" ", found.Problems);
        return RedirectToPage(new { id, tab = tab is ServicesTab or MotTab ? tab : null });
    }
}
