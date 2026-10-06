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

/// <summary>A stored vehicle. Pages use <c>VehicleView</c> / <c>VehicleInput</c> rather than this entity.</summary>
public class Vehicle : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    public VehicleType Type { get; set; } = VehicleType.Car;

    [Required, StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(15)]
    public string RegistrationNumber { get; set; } = string.Empty;

    public DateOnly? RegistrationDate { get; set; }
    public DateOnly? PurchasedDate { get; set; }

    public List<FuelEntry> FuelEntries { get; set; } = [];
}
