using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.FuelLog;

/// <summary>Shared plumbing for the Create and Edit fill-up pages.</summary>
public abstract class FuelFormPageModel(AppDbContext db, FuelStops stops) : PageModel
{
    /// <summary>The previous fill-up, with the brand and station it finished at (its last stop).</summary>
    public record PreviousFillUp(DateOnly Date, int? Odometer, string? Brand, string? Station);

    public record StationOption(string Name, string Brand);

    protected AppDbContext Db => db;
    protected FuelStops Stops => stops;

    [BindProperty]
    public FuelEntryInput Entry { get; set; } = new();

    public List<SelectListItem> VehicleOptions { get; private set; } = [];
    public List<string> Brands { get; private set; } = [];
    public List<StationOption> Stations { get; private set; } = [];

    /// <summary>
    /// Station name (lower case) → its brand, for stations only one brand has. The form uses it to
    /// fill in the brand when a station is typed.
    /// </summary>
    public Dictionary<string, string> StationBrands { get; private set; } = [];

    /// <summary>The fill-up before this one for the selected vehicle (for odometer hints).</summary>
    public PreviousFillUp? PreviousEntry { get; private set; }

    /// <param name="editingId">The fill-up being edited, so it isn't treated as its own previous fill-up.</param>
    protected async Task LoadLookupsAsync(int? editingId = null)
    {
        VehicleOptions = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new SelectListItem($"{v.Make} {v.Model} ({v.RegistrationNumber})", v.Id.ToString()))
            .ToListAsync();

        Brands = await db.FuelBrands.OrderBy(b => b.Name).Select(b => b.Name).ToListAsync();
        Stations = await db.FuelStations.OrderBy(s => s.Name).ThenBy(s => s.FuelBrand!.Name)
            .Select(s => new StationOption(s.Name, s.FuelBrand!.Name)).ToListAsync();
        StationBrands = Stations.GroupBy(s => s.Name.ToLowerInvariant())
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Brand);

        // The form always shows at least one brand/station row.
        if (Entry.Stops.Count == 0) Entry.Stops.Add(new FuelStopInput());

        if (Entry.VehicleId != 0)
        {
            PreviousEntry = await db.FuelEntries
                .Where(f => f.VehicleId == Entry.VehicleId && f.Id != editingId && f.Date <= Entry.Date)
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(f => new PreviousFillUp(f.Date, f.Odometer,
                    f.Stops.OrderByDescending(s => s.Sequence).Select(s => s.FuelStation!.FuelBrand!.Name).FirstOrDefault(),
                    f.Stops.OrderByDescending(s => s.Sequence).Select(s => s.FuelStation!.Name).FirstOrDefault()))
                .FirstOrDefaultAsync();
        }
    }

    protected async Task ValidateAsync()
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == Entry.VehicleId))
            ModelState.AddModelError("Entry.VehicleId", "Choose a vehicle.");
        if (Entry.VoucherPerLitre is { } voucher && voucher >= Entry.CostPerLitre)
            ModelState.AddModelError("Entry.VoucherPerLitre", "Voucher discount must be less than the price per litre.");
        if (Entry.Date > DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
            ModelState.AddModelError("Entry.Date", "Date can't be in the future.");
        await stops.ValidateAsync(Entry.Stops, ModelState, "Entry.Stops");
    }
}
