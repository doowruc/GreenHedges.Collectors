using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelLog;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? VehicleId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }

    public List<FuelEntryView> Entries { get; private set; } = [];
    public FuelSummary Summary { get; private set; } = FuelSummary.Empty;
    public List<SelectListItem> VehicleOptions { get; private set; } = [];
    public List<SelectListItem> YearOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        VehicleOptions = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new SelectListItem($"{v.Make} {v.Model}", v.Id.ToString(), v.Id == VehicleId))
            .ToListAsync();

        var query = db.FuelEntries.AsQueryable();
        if (VehicleId is { } vid) query = query.Where(f => f.VehicleId == vid);

        YearOptions = (await query.Select(f => f.Date.Year).Distinct().OrderByDescending(y => y).ToListAsync())
            .Select(y => new SelectListItem(y.ToString(), y.ToString(), y == Year))
            .ToList();

        if (Year is { } yr) query = query.Where(f => f.Date.Year == yr);
        Entries = await query
            // Same order DataTables starts with (data-table.js): newest first, then by vehicle.
            .OrderByDescending(f => f.Date).ThenBy(f => f.Vehicle!.Make).ThenBy(f => f.Vehicle!.Model).ThenByDescending(f => f.Id)
            .Select(FuelEntryView.Projection)
            .ToListAsync();
        Summary = stats.Summarise(Entries);
    }
}
