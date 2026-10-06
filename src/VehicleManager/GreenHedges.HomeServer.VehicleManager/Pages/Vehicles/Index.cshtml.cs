using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Vehicles;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(Vehicle Vehicle, int FillUps);

    public List<Row> Vehicles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Vehicles = await db.Vehicles
            .Include(v => v.Owner)
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new Row(v, v.FuelEntries.Count))
            .ToListAsync();
    }
}
