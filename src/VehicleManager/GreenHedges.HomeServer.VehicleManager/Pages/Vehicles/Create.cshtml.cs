using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Vehicles;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public VehicleInput Vehicle { get; set; } = new();

    public async Task OnGetAsync()
    {
        var owners = await VehicleRules.OwnerOptionsAsync(db);
        ViewData["OwnerOptions"] = owners;
        // With a single owner there's nothing to choose, so pre-select it.
        if (owners.Count == 1) Vehicle.OwnerId = int.Parse(owners[0].Value);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await VehicleRules.ValidateAsync(db, null, Vehicle, ModelState);
        if (!ModelState.IsValid)
        {
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            return Page();
        }

        var vehicle = new Vehicle();
        Vehicle.ApplyTo(vehicle);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {Vehicle.DisplayName} ({Vehicle.RegistrationNumber}).";
        return RedirectToPage("Details", new { id = vehicle.Id });
    }
}
