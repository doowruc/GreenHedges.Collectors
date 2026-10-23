using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelLog;

public class DeleteModel(AppDbContext db) : PageModel
{
    public FuelEntryView Entry { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await db.FuelEntries.Where(f => f.Id == id).Select(FuelEntryView.Projection).FirstOrDefaultAsync();
        if (entry is null) return NotFound();
        Entry = entry;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var vehicleId = await db.FuelEntries.Where(f => f.Id == id).Select(f => (int?)f.VehicleId).FirstOrDefaultAsync();
        if (vehicleId is null) return RedirectToPage("Index");

        await db.FuelEntries.Where(f => f.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = "Fill-up deleted.";
        return RedirectToPage("/Vehicles/Details", new { id = vehicleId });
    }
}
