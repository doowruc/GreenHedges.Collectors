using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Fuel;

public class EditModel(AppDbContext db) : FuelFormPageModel(db)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await Db.FuelEntries.Where(f => f.Id == id).Select(FuelEntryInput.Projection).FirstOrDefaultAsync();
        if (entry is null) return NotFound();
        Entry = entry;
        await LoadLookupsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await Db.FuelEntries.FindAsync(id);
        if (existing is null) return NotFound();

        await ValidateAsync();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(id);
            return Page();
        }

        Entry.ApplyTo(existing);
        await Db.SaveChangesAsync();
        TempData["Message"] = "Fill-up updated.";
        return RedirectToPage("/Vehicles/Details", new { id = Entry.VehicleId });
    }
}
