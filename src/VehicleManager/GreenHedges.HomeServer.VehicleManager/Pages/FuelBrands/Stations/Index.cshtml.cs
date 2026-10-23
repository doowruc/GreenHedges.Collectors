using Microsoft.AspNetCore.Mvc;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands.Stations;

public class IndexModel(FuelBrandList brands, FuelStationList stations) : StationPageModel(brands, stations)
{
    public List<FuelPlaceRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadBrandAsync()) return NotFound();
        Rows = await Stations.ListAsync(BrandId);
        return Page();
    }
}
