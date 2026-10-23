using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Owners;

internal static class OwnerRules
{
    public static string NormaliseName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Normalises the input and checks the name is unique, ignoring case
    /// (<paramref name="id"/> is null for a new owner).
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int? id, OwnerInput owner, ModelStateDictionary state)
    {
        owner.Name = NormaliseName(owner.Name);
        var name = owner.Name.ToLower();
        if (await db.Owners.AnyAsync(o => o.Id != id && o.Name.ToLower() == name))
            state.AddModelError("Owner.Name", "An owner with this name already exists.");
    }
}
