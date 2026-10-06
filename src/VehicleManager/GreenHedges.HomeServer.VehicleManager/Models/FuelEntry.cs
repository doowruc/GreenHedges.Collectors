using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VehicleManager.Models;

public class FuelEntry
{
    /// <summary>Litres in one UK (imperial) gallon.</summary>
    public const decimal LitresPerGallon = 4.54609m;

    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(50)]
    [Display(Name = "Fuel brand")]
    public string? Brand { get; set; }

    [StringLength(100)]
    [Display(Name = "Station location")]
    public string? Location { get; set; }

    [Range(0.01, 1000)]
    [Display(Name = "Litres")]
    public decimal Litres { get; set; }

    [Range(0.001, 100)]
    [Display(Name = "Cost per litre (£)")]
    public decimal CostPerLitre { get; set; }

    [Range(0, 100)]
    [Display(Name = "Voucher discount per litre (£)")]
    public decimal? VoucherPerLitre { get; set; }

    // --- Trip computer figures (as shown on the dash at fill-up) ---

    [Range(0, 200)]
    [Display(Name = "Trip computer avg MPG")]
    public decimal? TripAverageMpg { get; set; }

    [Range(0, 5000)]
    [Display(Name = "Trip miles")]
    public decimal? TripMiles { get; set; }

    [Range(0, 2000)]
    [Display(Name = "Range remaining (miles)")]
    public int? Range { get; set; }

    [Range(0, 2_000_000)]
    [Display(Name = "Odometer")]
    public int? Odometer { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    // --- Calculated values (not stored) ---

    [NotMapped]
    public decimal EffectiveCostPerLitre => CostPerLitre - (VoucherPerLitre ?? 0);

    [NotMapped]
    public decimal TotalCost => Math.Round(Litres * EffectiveCostPerLitre, 2);

    [NotMapped]
    public decimal Gallons => Litres / LitresPerGallon;

    /// <summary>Real MPG: trip miles since last fill divided by gallons put back in.</summary>
    [NotMapped]
    public decimal? CalculatedMpg =>
        TripMiles is > 0 && Litres > 0 ? Math.Round(TripMiles.Value / Gallons, 1) : null;

    /// <summary>How far the trip computer's MPG is from reality (positive = optimistic dash).</summary>
    [NotMapped]
    public decimal? MpgDifference =>
        CalculatedMpg is { } calc && TripAverageMpg is { } dash ? dash - calc : null;

    [NotMapped]
    public decimal? CostPerMile =>
        TripMiles is > 0 ? Math.Round(TotalCost / TripMiles.Value, 3) : null;
}
