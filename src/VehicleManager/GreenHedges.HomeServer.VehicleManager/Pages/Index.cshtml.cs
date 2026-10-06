using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Pages;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    public record VehicleCard(Vehicle Vehicle, FuelSummary AllTime, FuelSummary Year, FuelEntry? Last);

    public List<VehicleCard> Cards { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .Include(v => v.FuelEntries)
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .ToListAsync();

        var yearAgo = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        Cards = vehicles.Select(v => new VehicleCard(
                v,
                stats.Summarise(v.FuelEntries),
                stats.Summarise(v.FuelEntries, yearAgo),
                v.FuelEntries.OrderByDescending(f => f.Date).ThenByDescending(f => f.Id).FirstOrDefault()))
            .OrderByDescending(c => c.Last?.Date)
            .ToList();
    }
}
