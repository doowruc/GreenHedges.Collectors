using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Services;

public class DeleteModel(AppDbContext db) : ServicePageModel(db)
{
    public ServiceRecordView Service { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var service = await Db.ServiceRecords.Where(s => s.Id == id && s.VehicleId == VehicleId)
            .Select(ServiceRecordView.Projection).FirstOrDefaultAsync();
        if (service is null) return NotFound();
        Service = service;
        return Page();
    }

    /// <summary>Deletes the service. The next service's due date and mileage are left as they are.</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Db.ServiceRecords.Where(s => s.Id == id && s.VehicleId == VehicleId).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Service deleted.";
        return RedirectToVehicle();
    }
}
