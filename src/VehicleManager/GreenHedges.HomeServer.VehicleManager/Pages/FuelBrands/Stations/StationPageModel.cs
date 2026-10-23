using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands.Stations;

/// <summary>
/// Base for a fuel brand's Stations pages, routed under <c>/FuelBrands/{brandId}/Stations</c>.
/// Links and redirects between them must pass <c>brandId</c> (ASP.NET doesn't carry it over to a
/// different page, and the link comes out empty without it).
/// </summary>
public abstract class StationPageModel(FuelBrandList brands, FuelStationList stations) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int BrandId { get; set; }

    public string BrandName { get; private set; } = string.Empty;

    protected FuelBrandList Brands => brands;
    protected FuelStationList Stations => stations;

    /// <summary>Loads the brand from the URL; false if it isn't one of the user's.</summary>
    protected async Task<bool> LoadBrandAsync()
    {
        if (await brands.FindAsync(BrandId) is not { } brand) return false;
        BrandName = brand.Name;
        return true;
    }

    /// <summary>Loads the brand, and the station if it belongs to that brand.</summary>
    protected async Task<FuelPlaceRow?> LoadStationAsync(int id) =>
        await LoadBrandAsync() && await stations.FindAsync(id) is { } station && station.BrandId == BrandId ? station : null;

    protected RedirectToPageResult RedirectToList(int? brandId = null) =>
        RedirectToPage("/FuelBrands/Stations/Index", new { brandId = brandId ?? BrandId });
}
