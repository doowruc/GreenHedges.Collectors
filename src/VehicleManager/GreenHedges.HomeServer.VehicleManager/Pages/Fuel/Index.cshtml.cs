using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Pages.Fuel;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? VehicleId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }

    public List<FuelEntry> Entries { get; private set; } = [];
    public FuelSummary Summary { get; private set; } = FuelSummary.Empty;
    public List<SelectListItem> VehicleOptions { get; private set; } = [];
    public List<SelectListItem> YearOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        VehicleOptions = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new SelectListItem($"{v.Make} {v.Model}", v.Id.ToString(), v.Id == VehicleId))
            .ToListAsync();

        var query = db.FuelEntries.Include(f => f.Vehicle).AsQueryable();
        if (VehicleId is { } vid) query = query.Where(f => f.VehicleId == vid);

        var all = await query.OrderByDescending(f => f.Date).ThenByDescending(f => f.Id).ToListAsync();

        YearOptions = all.Select(f => f.Date.Year).Distinct().OrderDescending()
            .Select(y => new SelectListItem(y.ToString(), y.ToString(), y == Year))
            .ToList();

        Entries = Year is { } yr ? all.Where(f => f.Date.Year == yr).ToList() : all;
        Summary = stats.Summarise(Entries);
    }
}
