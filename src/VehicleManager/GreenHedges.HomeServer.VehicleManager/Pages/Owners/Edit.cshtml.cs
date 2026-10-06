using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Owners;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Owner Owner { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var owner = await db.Owners.FindAsync(id);
        if (owner is null) return NotFound();
        Owner = owner;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Owners.FindAsync(id);
        if (existing is null) return NotFound();

        Owner.Id = id;
        Owner.Name = OwnerRules.NormaliseName(Owner.Name);
        await OwnerRules.ValidateAsync(db, Owner, ModelState);

        if (!ModelState.IsValid) return Page();

        existing.Name = Owner.Name;
        await db.SaveChangesAsync();

        TempData["Message"] = "Owner updated.";
        return RedirectToPage("Index");
    }
}
