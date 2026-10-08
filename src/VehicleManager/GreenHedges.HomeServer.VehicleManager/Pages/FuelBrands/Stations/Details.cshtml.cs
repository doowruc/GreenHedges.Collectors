using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.FuelBrands.Stations;

/// <summary>A station's fill-ups and pump prices, split by fuel type.</summary>
public class DetailsModel(AppDbContext db, FuelBrandList brands, FuelStationList stations) : StationPageModel(brands, stations)
{
    public int StationId { get; private set; }
    public string StationName { get; private set; } = string.Empty;

    /// <summary>One per fuel type bought here, in <see cref="FuelType"/> order.</summary>
    public List<FuelTypeSection> Sections { get; private set; } = [];

    /// <summary>
    /// The section shown first: the one asked for (<c>?fuelType=Diesel</c>, as the fuel log tables link to it),
    /// otherwise the fuel type of the latest fill-up here.
    /// </summary>
    public FuelType Selected { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, FuelType? fuelType)
    {
        if (await LoadStationAsync(id) is not { } station) return NotFound();
        (StationId, StationName) = (station.Id, station.Name);

        // Every fill-up with a stop here, including split ones; newest first, as the table sorts them.
        var entries = await db.FuelEntries
            .Where(f => f.Stops.Any(s => s.FuelStationId == id))
            .OrderByDescending(f => f.Date).ThenBy(f => f.Vehicle!.Make).ThenBy(f => f.Vehicle!.Model).ThenByDescending(f => f.Id)
            .Select(FuelEntryView.Projection)
            .ToListAsync();

        Sections = entries.GroupBy(f => f.FuelType).OrderBy(g => g.Key)
            .Select(g => new FuelTypeSection(g.Key, g.ToList()))
            .ToList();
        if (entries.Count > 0)
            Selected = fuelType is { } asked && Sections.Any(s => s.FuelType == asked) ? asked : entries[0].FuelType;
        return Page();
    }
}

/// <param name="Entries">Newest first.</param>
public record FuelTypeSection(FuelType FuelType, List<FuelEntryView> Entries)
{
    /// <summary>
    /// Pump price (before any voucher, which is ours rather than the station's) by date, oldest first,
    /// for wwwroot/js/price-chart.js.
    /// </summary>
    public string ChartJson => JsonSerializer.Serialize(
        Entries.AsEnumerable().Reverse().Select(f => new { date = f.Date.ToString("yyyy-MM-dd"), price = f.CostPerLitre }));
}
