using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Fuel;

public class CreateModel(AppDbContext db, FuelStops stops) : FuelFormPageModel(db, stops)
{
    public async Task OnGetAsync(int? vehicleId)
    {
        // Default to the requested vehicle, otherwise the one most recently filled up.
        Entry.VehicleId = vehicleId
            ?? await Db.FuelEntries.OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                    .Select(f => (int?)f.VehicleId).FirstOrDefaultAsync()
            ?? await Db.Vehicles.Select(v => (int?)v.Id).FirstOrDefaultAsync()
            ?? 0;

        await LoadLookupsAsync();

        // Most fill-ups are at the usual station, so pre-fill from where the last one finished.
        if (PreviousEntry is { } prev)
            Entry.Stops = [new FuelStopInput { Brand = prev.Brand, Station = prev.Station }];
    }

    public async Task<IActionResult> OnPostAsync(bool addAnother = false)
    {
        await ValidateAsync();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return Page();
        }

        var entry = new FuelEntry();
        Entry.ApplyTo(entry);
        await Stops.SetStopsAsync(entry, Entry.Stops);
        Db.FuelEntries.Add(entry);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Fill-up on {Entry.Date:dd/MM/yyyy} saved ({Entry.TotalCost:C}).";

        return addAnother
            ? RedirectToPage("Create", new { vehicleId = Entry.VehicleId })
            : RedirectToPage("/Vehicles/Details", new { id = Entry.VehicleId });
    }
}
