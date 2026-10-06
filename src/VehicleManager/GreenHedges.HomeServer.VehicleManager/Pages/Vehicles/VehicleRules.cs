using Microsoft.AspNetCore.Mvc.ModelBinding;
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
}
