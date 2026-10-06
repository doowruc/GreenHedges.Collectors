using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Vehicles;

public class DeleteModel(AppDbContext db) : PageModel
{
    public Vehicle Vehicle { get; private set; } = new();
    public int FillUps { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.FindAsync(id);
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;
        FillUps = await db.FuelEntries.CountAsync(f => f.VehicleId == id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var vehicle = await db.Vehicles.FindAsync(id);
        if (vehicle is not null)
        {
            db.Vehicles.Remove(vehicle);
            await db.SaveChangesAsync();
            TempData["Message"] = $"Deleted {vehicle.DisplayName}.";
        }
        return RedirectToPage("Index");
    }
}
