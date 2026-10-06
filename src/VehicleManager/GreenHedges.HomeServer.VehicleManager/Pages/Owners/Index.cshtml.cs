using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Owners;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(Owner Owner, int Vehicles);

    public List<Row> Owners { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Owners = await db.Owners
            .OrderBy(o => o.Name)
            .Select(o => new Row(o, o.Vehicles.Count))
            .ToListAsync();
    }
}
