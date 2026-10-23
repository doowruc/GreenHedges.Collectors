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
    /// <summary>The vehicle's fuel type, i.e. what this fill-up bought.</summary>
    public FuelType FuelType { get; init; }
    public DateOnly Date { get; init; }
    public List<FuelStopView> Stops { get; init; } = [];
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
        FuelType = f.Vehicle.FuelType,
        Date = f.Date,
        Stops = f.Stops.OrderBy(s => s.Sequence)
            .Select(s => new FuelStopView(s.FuelStation!.FuelBrandId, s.FuelStation.FuelBrand!.Name, s.FuelStationId, s.FuelStation.Name))
            .ToList(),
        Litres = f.Litres,
        CostPerLitre = f.CostPerLitre,
        VoucherPerLitre = f.VoucherPerLitre,
        TripAverageMpg = f.TripAverageMpg,
        TripMiles = f.TripMiles,
        Range = f.Range,
        Odometer = f.Odometer
    };

    /// <summary>The brand, or brands joined with " + " for a split fill-up.</summary>
    public string Brand => Join(s => s.Brand);

    /// <summary>The station, or stations joined with " + " for a split fill-up (same order as <see cref="Brand"/>).</summary>
    public string Station => Join(s => s.Station);

    /// <summary>Brand and station together, e.g. "Shell Meadowbank"; "BP Oak Lane + Shell Meadowbank" for a split fill-up.</summary>
    public string FuelStation => Join(s => $"{s.Brand} {s.Station}");

    private string Join(Func<FuelStopView, string> name) => string.Join(" + ", Stops.Select(name));

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
