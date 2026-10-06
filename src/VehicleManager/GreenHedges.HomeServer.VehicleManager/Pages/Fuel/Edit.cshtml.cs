using Microsoft.AspNetCore.Mvc;
using VehicleManager.Data;

namespace VehicleManager.Pages.Fuel;

public class EditModel(AppDbContext db) : FuelFormPageModel(db)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await Db.FuelEntries.FindAsync(id);
        if (entry is null) return NotFound();
        Entry = entry;
        await LoadLookupsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await Db.FuelEntries.FindAsync(id);
        if (existing is null) return NotFound();

        Entry.Id = id;
        await ValidateAsync();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return Page();
        }

        Db.Entry(existing).CurrentValues.SetValues(Entry);
        await Db.SaveChangesAsync();
        TempData["Message"] = "Fill-up updated.";
        return RedirectToPage("/Vehicles/Details", new { id = Entry.VehicleId });
    }
}
