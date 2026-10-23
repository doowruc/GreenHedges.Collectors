using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, int Vehicles);

    public List<Row> Owners { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Owners = await db.Owners
            .OrderBy(o => o.Name)
            .Select(o => new Row(o.Id, o.Name, o.Vehicles.Count))
            .ToListAsync();
    }
}
