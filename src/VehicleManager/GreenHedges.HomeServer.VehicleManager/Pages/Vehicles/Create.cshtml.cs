using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Vehicles;

public class CreateModel(AppDbContext db, VehicleLookup lookup) : PageModel
{
    [BindProperty]
    public VehicleInput Vehicle { get; set; } = new();

    public async Task OnGetAsync()
    {
        var owners = await VehicleRules.OwnerOptionsAsync(db);
        ViewData["OwnerOptions"] = owners;
        ViewData["LookupNote"] = lookup.MorningCheck;
        // With a single owner there's nothing to choose, so pre-select it.
        if (owners.Count == 1) Vehicle.OwnerId = int.Parse(owners[0].Value);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await VehicleRules.ValidateAsync(db, null, Vehicle, ModelState);
        if (!ModelState.IsValid)
        {
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            ViewData["LookupNote"] = lookup.MorningCheck;
            return Page();
        }

        var vehicle = new Vehicle();
        Vehicle.ApplyTo(vehicle);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var message = $"Added {Vehicle.DisplayName} ({Vehicle.RegistrationNumber}).";

        // Fill in the MOT and tax dates if they weren't typed in. The vehicle is saved first, so a
        // lookup failing doesn't lose it.
        if (lookup.IsAvailable && vehicle.MotDueDate is null && vehicle.TaxDueDate is null)
        {
            var found = await lookup.RefreshAsync(vehicle);
            await db.SaveChangesAsync();
            message += $" Checked with {lookup.ServiceNames}: {found.Summary}.";
            if (found.Problems.Count > 0) TempData["Warning"] = string.Join(" ", found.Problems);
        }

        TempData["Message"] = message;
        return RedirectToPage("Details", new { id = vehicle.Id });
    }
}
