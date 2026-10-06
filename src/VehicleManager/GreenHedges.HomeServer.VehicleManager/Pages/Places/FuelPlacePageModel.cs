using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VehicleManager.Pages.Places;

/// <summary>
/// Base for the shared Brands/Stations pages, routed as <c>/{kind}/...</c> where kind is "brands"
/// or "stations" (see each page's <c>@page</c>). The kind picks which list they work on.
/// </summary>
/// <remarks>
/// Links and redirects between these pages must pass the kind (<c>asp-route-kind="@Model.Kind"</c>,
/// <see cref="RedirectToList"/>): ASP.NET doesn't carry a route value over to a different page,
/// so without it the link can't be built and comes out empty.
/// </remarks>
public abstract class FuelPlacePageModel(FuelBrandList brands, FuelStationList stations) : PageModel
{
    public IFuelPlaceList Places =>
        string.Equals(RouteData.Values["kind"] as string, "stations", StringComparison.OrdinalIgnoreCase) ? stations : brands;

    protected FuelBrandList Brands => brands;

    /// <summary>The route value for these pages: "brands" or "stations".</summary>
    public string Kind => Places.Plural.ToLowerInvariant();

    protected RedirectToPageResult RedirectToList() => RedirectToPage("Index", new { kind = Kind });
}
