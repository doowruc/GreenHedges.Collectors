using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Vehicles;

internal static class VehicleRules
{
    public static string NormaliseReg(string? reg) =>
        string.Join(' ', (reg ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static void ValidateDates(Vehicle v, ModelStateDictionary state)
    {
        if (v.RegistrationDate is { } reg && v.PurchasedDate is { } bought && bought < reg)
            state.AddModelError("Vehicle.PurchasedDate", "Purchased date can't be before the registration date.");
    }

    public static async Task ValidateOwnerAsync(AppDbContext db, Vehicle v, ModelStateDictionary state)
    {
        if (!await db.Owners.AnyAsync(o => o.Id == v.OwnerId))
            state.AddModelError("Vehicle.OwnerId", "Choose an owner.");
    }

    /// <summary>Owner choices for <c>_VehicleForm</c>, which reads them from <c>ViewData["OwnerOptions"]</c>.</summary>
    public static async Task<List<SelectListItem>> OwnerOptionsAsync(AppDbContext db) =>
        await db.Owners.OrderBy(o => o.Name)
            .Select(o => new SelectListItem(o.Name, o.Id.ToString()))
            .ToListAsync();
}
