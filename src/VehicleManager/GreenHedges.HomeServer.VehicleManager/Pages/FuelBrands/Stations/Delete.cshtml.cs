using Microsoft.AspNetCore.Mvc;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands.Stations;

public class DeleteModel(FuelBrandList brands, FuelStationList stations) : StationPageModel(brands, stations)
{
    public FuelPlaceRow Station { get; private set; } = new(0, "", null, null, 0, null);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadStationAsync(id) is not { } station) return NotFound();
        Station = station;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadStationAsync(id) is not { } station) return RedirectToList();
        if (!await Stations.DeleteIfUnusedAsync(id)) return RedirectToPage("Delete", new { brandId = BrandId, id });

        TempData["Message"] = $"Deleted {station.Name}.";
        return RedirectToList();
    }
}
