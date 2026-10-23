using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands.Stations;

public class MergeModel(FuelBrandList brands, FuelStationList stations) : StationPageModel(brands, stations)
{
    public FuelPlaceRow Source { get; private set; } = new(0, "", null, null, 0, null);

    /// <summary>Any other station, this brand's first; another brand's station fixes one filed under the wrong brand.</summary>
    public List<SelectListItem> Targets { get; private set; } = [];

    [BindProperty]
    public int? IntoId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Targets.SingleOrDefault(t => t.Value == IntoId?.ToString()) is not { } into)
        {
            ModelState.AddModelError(nameof(IntoId), "Choose the station to merge it into.");
            return Page();
        }

        var moved = await Stations.MergeAsync(id, IntoId!.Value);
        TempData["Message"] = $"Merged {Source.Name} into {into.Text} ({moved} fill-up(s) moved).";
        return RedirectToList();
    }

    private async Task<bool> LoadAsync(int id)
    {
        if (await LoadStationAsync(id) is not { } source) return false;
        Source = source;
        Targets = (await Stations.ListAsync())
            .Where(s => s.Id != id)
            .OrderBy(s => s.BrandId != BrandId)
            .Select(s => new SelectListItem(s.BrandId == BrandId ? s.Name : s.FullName, s.Id.ToString(), s.Id == IntoId))
            .ToList();
        return true;
    }
}
