using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Fuel;

public class DeleteModel(AppDbContext db) : PageModel
{
    public FuelEntry Entry { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await db.FuelEntries.Include(f => f.Vehicle).FirstOrDefaultAsync(f => f.Id == id);
        if (entry is null) return NotFound();
        Entry = entry;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var entry = await db.FuelEntries.FindAsync(id);
        if (entry is null) return RedirectToPage("Index");

        db.FuelEntries.Remove(entry);
        await db.SaveChangesAsync();
        TempData["Message"] = "Fill-up deleted.";
        return RedirectToPage("/Vehicles/Details", new { id = entry.VehicleId });
    }
}
