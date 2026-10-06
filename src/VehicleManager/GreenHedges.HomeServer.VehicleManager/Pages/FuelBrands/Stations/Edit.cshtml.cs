using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.FuelBrands.Stations;

public class EditModel(FuelBrandList brands, FuelStationList stations) : StationPageModel(brands, stations)
{
    public override string Crumb => "Edit station";

    [BindProperty]
    public FuelPlaceInput Place { get; set; } = new();

    public List<SelectListItem> BrandOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadStationAsync(id) is null || await Stations.GetForEditAsync(id) is not { } station) return NotFound();
        Place = station;
        await LoadBrandOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadStationAsync(id) is null) return NotFound();
        await Stations.ValidateAsync(id, Place, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadBrandOptionsAsync();
            return Page();
        }

        var (result, rowVersion) = await Stations.SaveAsync(id, Place);
        if (result == SaveResult.NotFound) return NotFound();
        if (result == SaveResult.Conflict)
        {
            ModelState.AddModelError(string.Empty,
                "Someone changed this station after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Place.RowVersion = rowVersion;
            ModelState.Remove("Place.RowVersion");
            await LoadBrandOptionsAsync();
            return Page();
        }

        TempData["Message"] = $"Saved {Place.Name}.";
        return RedirectToList(Place.BrandId);   // the brand it's now under
    }

    private async Task LoadBrandOptionsAsync() =>
        BrandOptions = (await Brands.ListAsync()).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
}
