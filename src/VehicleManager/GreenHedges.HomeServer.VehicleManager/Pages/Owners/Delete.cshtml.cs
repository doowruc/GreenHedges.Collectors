using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Owners;

public class DeleteModel(AppDbContext db) : PageModel
{
    public Owner Owner { get; private set; } = new();

    /// <summary>Vehicles still owned; the owner can't be deleted until they are reassigned.</summary>
    public List<Vehicle> Vehicles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var owner = await db.Owners.FindAsync(id);
        if (owner is null) return NotFound();
        Owner = owner;
        Vehicles = await db.Vehicles.Where(v => v.OwnerId == id)
            .OrderBy(v => v.Make).ThenBy(v => v.Model).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var owner = await db.Owners.FindAsync(id);
        if (owner is null) return RedirectToPage("Index");
        if (await db.Vehicles.AnyAsync(v => v.OwnerId == id)) return RedirectToPage("Delete", new { id });

        db.Owners.Remove(owner);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Deleted {owner.Name}.";
        return RedirectToPage("Index");
    }
}
