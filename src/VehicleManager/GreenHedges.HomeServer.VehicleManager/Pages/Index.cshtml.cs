using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    public record VehicleCard(VehicleView Vehicle, FuelSummary AllTime, FuelSummary Year, FuelEntryView? Last);

    public List<VehicleCard> Cards { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        var entries = (await db.FuelEntries
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(FuelEntryView.Projection)
                .ToListAsync())
            .ToLookup(f => f.VehicleId);

        var yearAgo = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        Cards = vehicles.Select(v => new VehicleCard(
                v,
                stats.Summarise(entries[v.Id]),
                stats.Summarise(entries[v.Id], yearAgo),
                entries[v.Id].FirstOrDefault()))
            .OrderByDescending(c => c.Last?.Date)
            .ToList();
    }
}
