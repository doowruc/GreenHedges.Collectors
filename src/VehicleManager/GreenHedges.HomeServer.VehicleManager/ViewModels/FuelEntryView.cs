using System.Linq.Expressions;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.ViewModels;

/// <summary>A fill-up for display, with the figures calculated from its raw data.</summary>
public class FuelEntryView
{
    public int Id { get; init; }
    public int VehicleId { get; init; }
    public string VehicleName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string? Brand { get; init; }
    public string? Location { get; init; }
    public decimal Litres { get; init; }
    public decimal CostPerLitre { get; init; }
    public decimal? VoucherPerLitre { get; init; }
    public decimal? TripAverageMpg { get; init; }
    public decimal? TripMiles { get; init; }
    public int? Range { get; init; }
    public int? Odometer { get; init; }

    public static readonly Expression<Func<FuelEntry, FuelEntryView>> Projection = f => new FuelEntryView
    {
        Id = f.Id,
        VehicleId = f.VehicleId,
        VehicleName = f.Vehicle!.Make + " " + f.Vehicle.Model,
        Date = f.Date,
        Brand = f.Brand,
        Location = f.Location,
        Litres = f.Litres,
        CostPerLitre = f.CostPerLitre,
        VoucherPerLitre = f.VoucherPerLitre,
        TripAverageMpg = f.TripAverageMpg,
        TripMiles = f.TripMiles,
        Range = f.Range,
        Odometer = f.Odometer
    };

    // --- Calculated values ---

    public decimal EffectiveCostPerLitre => FuelCalculations.EffectiveCostPerLitre(CostPerLitre, VoucherPerLitre);

    public decimal TotalCost => FuelCalculations.TotalCost(Litres, CostPerLitre, VoucherPerLitre);

    public decimal Gallons => Litres / FuelCalculations.LitresPerGallon;

    /// <summary>Real MPG: trip miles since last fill divided by gallons put back in.</summary>
    public decimal? CalculatedMpg =>
        TripMiles is > 0 && Litres > 0 ? Math.Round(TripMiles.Value / Gallons, 1) : null;

    /// <summary>How far the trip computer's MPG is from reality (positive = optimistic dash).</summary>
    public decimal? MpgDifference =>
        CalculatedMpg is { } calc && TripAverageMpg is { } dash ? dash - calc : null;

    public decimal? CostPerMile =>
        TripMiles is > 0 ? Math.Round(TotalCost / TripMiles.Value, 3) : null;
}
