using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Owners;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Owner Owner { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        Owner.Id = 0;
        Owner.Name = OwnerRules.NormaliseName(Owner.Name);
        await OwnerRules.ValidateAsync(db, Owner, ModelState);

        if (!ModelState.IsValid) return Page();

        db.Owners.Add(Owner);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {Owner.Name}.";
        return RedirectToPage("Index");
    }
}
