using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Pages.Vehicles.Services;

/// <summary>
/// Base for a vehicle's service history pages, routed under <c>/Vehicles/{vehicleId}/Services</c>.
/// Links and redirects between them must pass <c>vehicleId</c> (ASP.NET doesn't carry it over to a
/// different page, and the link comes out empty without it).
/// </summary>
public abstract class ServicePageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int VehicleId { get; set; }

    public string VehicleName { get; private set; } = string.Empty;
    public string RegistrationNumber { get; private set; } = string.Empty;

    /// <summary>When the user bought the vehicle: services before it were under a previous owner.</summary>
    public DateOnly? PurchasedDate { get; private set; }

    protected AppDbContext Db => db;

    /// <summary>Loads the vehicle from the URL; false if it isn't one of the user's.</summary>
    protected async Task<bool> LoadVehicleAsync()
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == VehicleId)
            .Select(v => new { v.Make, v.Model, v.RegistrationNumber, v.PurchasedDate }).FirstOrDefaultAsync();
        if (vehicle is null) return false;
        VehicleName = $"{vehicle.Make} {vehicle.Model}".Trim();
        RegistrationNumber = vehicle.RegistrationNumber;
        PurchasedDate = vehicle.PurchasedDate;
        // For _ServiceForm, which ticks "under a previous owner" for dates before it.
        ViewData["PurchasedDate"] = vehicle.PurchasedDate;
        return true;
    }

    /// <summary>
    /// Suggestions for <c>_ServiceForm</c>, which reads them from <c>ViewData</c>: every garage used on any vehicle's
    /// services (<c>"Garages"</c>), and each one's locations, keyed in lower case (<c>"LocationsByGarage"</c>).
    /// </summary>
    protected async Task LoadSuggestionsAsync()
    {
        var used = await db.ServiceRecords.Select(s => new { s.Garage, s.GarageLocation }).Distinct().ToListAsync();
        ViewData["Garages"] = JsonSerializer.Serialize(used.Select(u => u.Garage).Distinct(StringComparer.OrdinalIgnoreCase).Order());
        ViewData["LocationsByGarage"] = JsonSerializer.Serialize(used
            .Where(u => u.GarageLocation is not null)
            .GroupBy(u => u.Garage.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Select(u => u.GarageLocation!).Distinct(StringComparer.OrdinalIgnoreCase).Order()));
    }

    /// <summary>Back to the vehicle's page, on its Services tab.</summary>
    protected RedirectToPageResult RedirectToVehicle() => RedirectToPage("/Vehicles/Details", new { id = VehicleId, tab = DetailsModel.ServicesTab });
}
