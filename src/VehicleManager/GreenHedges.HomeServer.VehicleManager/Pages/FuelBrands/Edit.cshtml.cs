using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.FuelBrands;

public class EditModel(FuelBrandList brands) : PageModel
{
    [BindProperty]
    public FuelPlaceInput Place { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await brands.GetForEditAsync(id) is not { } brand) return NotFound();
        Place = brand;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await brands.ValidateAsync(id, Place, ModelState);
        if (!ModelState.IsValid) return Page();

        var (result, rowVersion) = await brands.SaveAsync(id, Place);
        if (result == SaveResult.NotFound) return NotFound();
        if (result == SaveResult.Conflict)
        {
            ModelState.AddModelError(string.Empty,
                "Someone changed this fuel brand after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Place.RowVersion = rowVersion;
            ModelState.Remove("Place.RowVersion");
            return Page();
        }

        TempData["Message"] = $"Saved {Place.Name}.";
        return RedirectToPage("Index");
    }
}
