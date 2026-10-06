using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Owners;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public OwnerInput Owner { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var owner = await db.Owners.Where(o => o.Id == id).Select(OwnerInput.Projection).FirstOrDefaultAsync();
        if (owner is null) return NotFound();
        Owner = owner;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Owners.FirstOrDefaultAsync(o => o.Id == id);
        if (existing is null) return NotFound();

        await OwnerRules.ValidateAsync(db, id, Owner, ModelState);
        if (!ModelState.IsValid) return Page();

        Owner.ApplyTo(existing);
        await db.SaveChangesAsync();

        TempData["Message"] = "Owner updated.";
        return RedirectToPage("Index");
    }
}
