using System.ComponentModel.DataAnnotations;

namespace VehicleManager.Models;

/// <summary>A stored fill-up. Pages use <c>FuelEntryView</c> / <c>FuelEntryInput</c> rather than this entity.</summary>
public class FuelEntry
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateOnly Date { get; set; }

    [StringLength(50)]
    public string? Brand { get; set; }

    [StringLength(100)]
    public string? Location { get; set; }

    public decimal Litres { get; set; }
    public decimal CostPerLitre { get; set; }
    public decimal? VoucherPerLitre { get; set; }

    // --- Trip computer figures (as shown on the dash at fill-up) ---
    public decimal? TripAverageMpg { get; set; }
    public decimal? TripMiles { get; set; }
    public int? Range { get; set; }
    public int? Odometer { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
