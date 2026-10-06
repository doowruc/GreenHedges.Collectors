using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Vehicles;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Vehicle Vehicle { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.FindAsync(id);
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Vehicles.FindAsync(id);
        if (existing is null) return NotFound();

        Vehicle.Id = id;
        Vehicle.RegistrationNumber = VehicleRules.NormaliseReg(Vehicle.RegistrationNumber);
        if (await db.Vehicles.AnyAsync(v => v.Id != id && v.RegistrationNumber == Vehicle.RegistrationNumber))
            ModelState.AddModelError("Vehicle.RegistrationNumber", "Another vehicle already has this registration.");
        VehicleRules.ValidateDates(Vehicle, ModelState);

        if (!ModelState.IsValid) return Page();

        existing.Type = Vehicle.Type;
        existing.Make = Vehicle.Make;
        existing.Model = Vehicle.Model;
        existing.RegistrationNumber = Vehicle.RegistrationNumber;
        existing.RegistrationDate = Vehicle.RegistrationDate;
        existing.PurchasedDate = Vehicle.PurchasedDate;
        await db.SaveChangesAsync();

        TempData["Message"] = "Vehicle updated.";
        return RedirectToPage("Details", new { id });
    }
}
