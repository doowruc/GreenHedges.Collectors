using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public OwnerInput Owner { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await OwnerRules.ValidateAsync(db, null, Owner, ModelState);
        if (!ModelState.IsValid) return Page();

        var owner = new Owner();
        Owner.ApplyTo(owner);
        db.Owners.Add(owner);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {owner.Name}.";
        return RedirectToPage("Index");
    }
}
