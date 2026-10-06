using Microsoft.AspNetCore.Mvc;

namespace VehicleManager.Pages.Places;

public class DeleteModel(FuelBrandList brands, FuelStationList stations) : FuelPlacePageModel(brands, stations)
{
    public FuelPlaceRow Place { get; private set; } = new(0, "", null, 0, null);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await Places.FindAsync(id) is not { } place) return NotFound();
        Place = place;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Places.FindAsync(id) is not { } place) return RedirectToList();
        if (!await Places.DeleteIfUnusedAsync(id)) return RedirectToPage("Delete", new { kind = Kind, id });

        TempData["Message"] = $"Deleted {place.FullName}.";
        return RedirectToList();
    }
}
