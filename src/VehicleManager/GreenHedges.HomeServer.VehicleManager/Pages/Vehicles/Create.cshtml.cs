using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Vehicles;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Vehicle Vehicle { get; set; } = new();

    public async Task OnGetAsync()
    {
        var owners = await VehicleRules.OwnerOptionsAsync(db);
        ViewData["OwnerOptions"] = owners;
        // With a single owner there's nothing to choose, so pre-select it.
        if (owners.Count == 1) Vehicle.OwnerId = int.Parse(owners[0].Value);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Vehicle.RegistrationNumber = VehicleRules.NormaliseReg(Vehicle.RegistrationNumber);
        if (await db.Vehicles.AnyAsync(v => v.RegistrationNumber == Vehicle.RegistrationNumber))
            ModelState.AddModelError("Vehicle.RegistrationNumber", "A vehicle with this registration already exists.");
        VehicleRules.ValidateDates(Vehicle, ModelState);
        await VehicleRules.ValidateOwnerAsync(db, Vehicle, ModelState);

        if (!ModelState.IsValid)
        {
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            return Page();
        }

        db.Vehicles.Add(Vehicle);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {Vehicle.DisplayName} ({Vehicle.RegistrationNumber}).";
        return RedirectToPage("Details", new { id = Vehicle.Id });
    }
}
