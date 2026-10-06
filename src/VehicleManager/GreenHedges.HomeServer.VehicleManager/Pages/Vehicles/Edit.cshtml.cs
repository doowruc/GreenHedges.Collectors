using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Vehicles;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public VehicleInput Vehicle { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleInput.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;
        ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (existing is null) return NotFound();

        await VehicleRules.ValidateAsync(db, id, Vehicle, ModelState);
        if (!ModelState.IsValid)
        {
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            return Page();
        }

        Vehicle.ApplyTo(existing);
        await db.SaveChangesAsync();

        TempData["Message"] = "Vehicle updated.";
        return RedirectToPage("Details", new { id });
    }
}
