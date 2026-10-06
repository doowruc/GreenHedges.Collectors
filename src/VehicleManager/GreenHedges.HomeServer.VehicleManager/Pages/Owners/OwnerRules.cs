using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Pages.Owners;

internal static class OwnerRules
{
    public static string NormaliseName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Owner names must be unique, ignoring case.</summary>
    public static async Task ValidateAsync(AppDbContext db, Owner owner, ModelStateDictionary state)
    {
        var name = owner.Name.ToLower();
        if (await db.Owners.AnyAsync(o => o.Id != owner.Id && o.Name.ToLower() == name))
            state.AddModelError("Owner.Name", "An owner with this name already exists.");
    }
}
