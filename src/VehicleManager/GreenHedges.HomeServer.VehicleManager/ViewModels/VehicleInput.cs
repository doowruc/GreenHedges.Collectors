using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using VehicleManager.Models;

namespace VehicleManager.ViewModels;

public class VehicleInput
{
    [Display(Name = "Owner")]
    public int OwnerId { get; set; }

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

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public string DisplayName => $"{Make} {Model}".Trim();

    public static readonly Expression<Func<Vehicle, VehicleInput>> Projection = v => new VehicleInput
    {
        OwnerId = v.OwnerId,
        Type = v.Type,
        Make = v.Make,
        Model = v.Model,
        RegistrationNumber = v.RegistrationNumber,
        RegistrationDate = v.RegistrationDate,
        PurchasedDate = v.PurchasedDate,
        RowVersion = v.RowVersion
    };

    public void ApplyTo(Vehicle vehicle)
    {
        vehicle.OwnerId = OwnerId;
        vehicle.Type = Type;
        vehicle.Make = Make;
        vehicle.Model = Model;
        vehicle.RegistrationNumber = RegistrationNumber;
        vehicle.RegistrationDate = RegistrationDate;
        vehicle.PurchasedDate = PurchasedDate;
    }
}
