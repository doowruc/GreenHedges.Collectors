using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Fuel;

/// <summary>Shared plumbing for the Create and Edit fill-up pages.</summary>
public abstract class FuelFormPageModel(AppDbContext db) : PageModel
{
    public record PreviousFillUp(DateOnly Date, int? Odometer, string? Brand, string? Location);

    protected AppDbContext Db => db;

    [BindProperty]
    public FuelEntryInput Entry { get; set; } = new();

    public List<SelectListItem> VehicleOptions { get; private set; } = [];
    public List<string> Brands { get; private set; } = [];
    public List<string> Locations { get; private set; } = [];

    /// <summary>The fill-up before this one for the selected vehicle (for odometer hints).</summary>
    public PreviousFillUp? PreviousEntry { get; private set; }

    /// <param name="editingId">The fill-up being edited, so it isn't treated as its own previous fill-up.</param>
    protected async Task LoadLookupsAsync(int? editingId = null)
    {
        VehicleOptions = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new SelectListItem($"{v.Make} {v.Model} ({v.RegistrationNumber})", v.Id.ToString()))
            .ToListAsync();

        Brands = await db.FuelEntries.Where(f => f.Brand != null)
            .Select(f => f.Brand!).Distinct().OrderBy(b => b).ToListAsync();
        Locations = await db.FuelEntries.Where(f => f.Location != null)
            .Select(f => f.Location!).Distinct().OrderBy(l => l).ToListAsync();

        if (Entry.VehicleId != 0)
        {
            PreviousEntry = await db.FuelEntries
                .Where(f => f.VehicleId == Entry.VehicleId && f.Id != editingId && f.Date <= Entry.Date)
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(f => new PreviousFillUp(f.Date, f.Odometer, f.Brand, f.Location))
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
    }
}
