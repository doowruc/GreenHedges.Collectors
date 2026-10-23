using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

public class IndexModel(FuelBrandList brands) : PageModel
{
    public List<FuelPlaceRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await brands.ListAsync();
}
