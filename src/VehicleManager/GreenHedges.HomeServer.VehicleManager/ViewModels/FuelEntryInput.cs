using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.ViewModels;

/// <summary>The editable fields of a fill-up, used by the fill-up form and the spreadsheet import.</summary>
public class FuelEntryInput
{
    public int VehicleId { get; set; }

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

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public decimal TotalCost => FuelCalculations.TotalCost(Litres, CostPerLitre, VoucherPerLitre);

    public static readonly Expression<Func<FuelEntry, FuelEntryInput>> Projection = f => new FuelEntryInput
    {
        VehicleId = f.VehicleId,
        Date = f.Date,
        Brand = f.Brand,
        Location = f.Location,
        Litres = f.Litres,
        CostPerLitre = f.CostPerLitre,
        VoucherPerLitre = f.VoucherPerLitre,
        TripAverageMpg = f.TripAverageMpg,
        TripMiles = f.TripMiles,
        Range = f.Range,
        Odometer = f.Odometer,
        Notes = f.Notes,
        RowVersion = f.RowVersion
    };

    public void ApplyTo(FuelEntry entry)
    {
        entry.VehicleId = VehicleId;
        entry.Date = Date;
        entry.Brand = Brand;
        entry.Location = Location;
        entry.Litres = Litres;
        entry.CostPerLitre = CostPerLitre;
        entry.VoucherPerLitre = VoucherPerLitre;
        entry.TripAverageMpg = TripAverageMpg;
        entry.TripMiles = TripMiles;
        entry.Range = Range;
        entry.Odometer = Odometer;
        entry.Notes = Notes;
    }
}
