using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace VehicleManager.Pages.FuelBrands;

public class MergeModel(FuelBrandList brands) : PageModel
{
    public FuelPlaceRow Source { get; private set; } = new(0, "", null, null, 0, null);
    public List<SelectListItem> Targets { get; private set; } = [];

    [BindProperty]
    public int? IntoId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Targets.SingleOrDefault(t => t.Value == IntoId?.ToString()) is not { } into)
        {
            ModelState.AddModelError(nameof(IntoId), "Choose the fuel brand to merge it into.");
            return Page();
        }

        var moved = await brands.MergeAsync(id, IntoId!.Value);
        TempData["Message"] = $"Merged {Source.Name} into {into.Text} ({moved} fill-up stop(s) moved).";
        return RedirectToPage("Index");
    }

    private async Task<bool> LoadAsync(int id)
    {
        if (await brands.FindAsync(id) is not { } source) return false;
        Source = source;
        Targets = (await brands.ListAsync()).Where(b => b.Id != id)
            .Select(b => new SelectListItem(b.Name, b.Id.ToString(), b.Id == IntoId)).ToList();
        return true;
    }
}
