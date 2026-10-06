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

public class Vehicle
{
    public int Id { get; set; }

    [Display(Name = "Owner")]
    public int OwnerId { get; set; }

    public Owner? Owner { get; set; }

    [Required]
    public VehicleType Type { get; set; } = VehicleType.Car;

    [Required, StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(15)]
    [Display(Name = "Registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Registration date")]
    public DateOnly? RegistrationDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Purchased date")]
    public DateOnly? PurchasedDate { get; set; }

    public List<FuelEntry> FuelEntries { get; set; } = [];

    public string DisplayName => $"{Make} {Model}".Trim();
}
