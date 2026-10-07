using System.ComponentModel.DataAnnotations;

namespace VehicleManager.Models;

public enum VehicleType
{
    Car,
    Van,
    Motorcycle,
    Motorhome,
    Other
}

public enum FuelType
{
    Unleaded,
    Diesel
}

/// <summary>A stored vehicle. Pages use <c>VehicleView</c> / <c>VehicleInput</c> rather than this entity.</summary>
public class Vehicle : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    public VehicleType Type { get; set; } = VehicleType.Car;
    public FuelType FuelType { get; set; } = FuelType.Unleaded;

    [Required, StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(15)]
    public string RegistrationNumber { get; set; } = string.Empty;

    public DateOnly? RegistrationDate { get; set; }
    public DateOnly? PurchasedDate { get; set; }

    // --- When things are next due. MOT and tax can be looked up (Services/VehicleLookup); insurance and service are typed in. ---
    public DateOnly? MotDueDate { get; set; }
    public DateOnly? TaxDueDate { get; set; }
    public DateOnly? InsuranceDueDate { get; set; }
    public DateOnly? ServiceDueDate { get; set; }

    public List<FuelEntry> FuelEntries { get; set; } = [];
}
