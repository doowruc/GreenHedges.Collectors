using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands.Stations;

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
