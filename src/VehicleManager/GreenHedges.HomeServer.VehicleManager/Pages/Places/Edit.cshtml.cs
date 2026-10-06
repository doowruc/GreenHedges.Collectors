using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Places;

public class EditModel(FuelBrandList brands, FuelStationList stations) : FuelPlacePageModel(brands, stations)
{
    [BindProperty]
    public FuelPlaceInput Place { get; set; } = new();

    /// <summary>Brand choices when editing a station.</summary>
    public List<SelectListItem> BrandOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await Places.GetForEditAsync(id) is not { } place) return NotFound();
        Place = place;
        await LoadBrandOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await Places.ValidateAsync(id, Place, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadBrandOptionsAsync();
            return Page();
        }

        var (result, rowVersion) = await Places.SaveAsync(id, Place);
        switch (result)
        {
            case SaveResult.NotFound:
                return NotFound();
            case SaveResult.Conflict:
                ModelState.AddModelError(string.Empty,
                    $"Someone changed this {Places.Singular} after you opened it, so your changes haven't been saved. " +
                    "Reload the page to see the latest version, or save again to overwrite it with yours.");
                Place.RowVersion = rowVersion;
                ModelState.Remove("Place.RowVersion");
                await LoadBrandOptionsAsync();
                return Page();
        }

        TempData["Message"] = $"Saved {Place.Name}.";
        return RedirectToList();
    }

    private async Task LoadBrandOptionsAsync()
    {
        if (Places.HasBrand)
            BrandOptions = (await Brands.ListAsync()).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
    }
}
