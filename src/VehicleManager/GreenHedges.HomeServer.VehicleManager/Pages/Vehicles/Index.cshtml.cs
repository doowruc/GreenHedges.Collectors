using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Vehicles;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(VehicleView Vehicle, int FillUps);

    public List<Row> Vehicles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        var fillUps = await db.FuelEntries
            .GroupBy(f => f.VehicleId)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Count);

        Vehicles = vehicles.Select(v => new Row(v, fillUps.GetValueOrDefault(v.Id))).ToList();
    }
}
