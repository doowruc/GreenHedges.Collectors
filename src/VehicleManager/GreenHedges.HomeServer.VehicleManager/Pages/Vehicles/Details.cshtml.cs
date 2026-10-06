using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Pages.Vehicles;

public class DetailsModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    public Vehicle Vehicle { get; private set; } = new();
    public List<FuelEntry> Entries { get; private set; } = [];
    public FuelSummary AllTime { get; private set; } = FuelSummary.Empty;
    public FuelSummary LastYear { get; private set; } = FuelSummary.Empty;
    public string ChartJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.FindAsync(id);
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;

        Entries = await db.FuelEntries
            .Where(f => f.VehicleId == id)
            .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
            .ToListAsync();

        AllTime = stats.Summarise(Entries);
        LastYear = stats.Summarise(Entries, DateOnly.FromDateTime(DateTime.Today.AddYears(-1)));

        ChartJson = JsonSerializer.Serialize(
            Entries.AsEnumerable().Reverse().Select(f => new
            {
                date = f.Date.ToString("dd/MM/yy"),
                mpg = f.CalculatedMpg,
                dash = f.TripAverageMpg,
                price = f.EffectiveCostPerLitre
            }));

        return Page();
    }
}
