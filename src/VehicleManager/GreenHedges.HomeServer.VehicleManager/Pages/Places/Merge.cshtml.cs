using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace VehicleManager.Pages.Places;

public class MergeModel(FuelBrandList brands, FuelStationList stations) : FuelPlacePageModel(brands, stations)
{
    public FuelPlaceRow Source { get; private set; } = new(0, "", null, 0, null);
    public List<SelectListItem> Targets { get; private set; } = [];

    [BindProperty]
    public int? IntoId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) =>
        await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (IntoId is not { } intoId || intoId == id || !Targets.Any(t => t.Value == intoId.ToString()))
        {
            ModelState.AddModelError(nameof(IntoId), $"Choose the {Places.Singular} to merge it into.");
            return Page();
        }

        var moved = await Places.MergeAsync(id, intoId);
        var into = Targets.Single(t => t.Value == intoId.ToString()).Text;
        TempData["Message"] = $"Merged {Source.Name} into {into} ({moved} fill-up stop(s) moved).";
        return RedirectToList();
    }

    private async Task<bool> LoadAsync(int id)
    {
        if (await Places.FindAsync(id) is not { } source) return false;
        Source = source;
        Targets = (await Places.ListAsync())
            .Where(p => p.Id != id)
            .Select(p => new SelectListItem(p.FullName, p.Id.ToString(), p.Id == IntoId))
            .ToList();
        return true;
    }
}
