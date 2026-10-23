using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Vehicles.Services;

public class CreateModel(AppDbContext db) : ServicePageModel(db)
{
    [BindProperty]
    public ServiceRecordInput Service { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadVehicleAsync()) return NotFound();
        await LoadSuggestionsAsync();
        // Start from today and the latest odometer reading.
        Service.Date = Reminders.UkToday();
        Service.Mileage = await Db.FuelEntries.Where(f => f.VehicleId == VehicleId).MaxAsync(f => f.Odometer);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadVehicleAsync()) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadSuggestionsAsync();
            return Page();
        }

        var vehicle = await Db.Vehicles.FirstAsync(v => v.Id == VehicleId);
        var service = new ServiceRecord { VehicleId = VehicleId };
        Service.ApplyTo(service);
        // Before the user bought it, it can only have been a previous owner's (the form ticks this too, as the date changes).
        if (service.Date < PurchasedDate) service.UnderPreviousOwner = true;
        Db.ServiceRecords.Add(service);
        var next = await ServiceSchedule.UpdateNextDueAsync(Db, vehicle, service);
        await Db.SaveChangesAsync();

        TempData["Message"] = $"Added the service on {service.Date:dd/MM/yyyy}." + (next is null ? "" : " " + next);
        return RedirectToVehicle();
    }
}
