using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

public class DeleteModel(FuelBrandList brands) : PageModel
{
    public FuelPlaceRow Brand { get; private set; } = new(0, "", null, null, 0, null);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await brands.FindAsync(id) is not { } brand) return NotFound();
        Brand = brand;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await brands.FindAsync(id) is not { } brand) return RedirectToPage("Index");
        if (!await brands.DeleteIfUnusedAsync(id)) return RedirectToPage("Delete", new { id });

        TempData["Message"] = $"Deleted {brand.Name}.";
        return RedirectToPage("Index");
    }
}
