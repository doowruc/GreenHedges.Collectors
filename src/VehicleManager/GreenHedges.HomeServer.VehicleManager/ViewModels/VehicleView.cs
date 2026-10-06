using System.Linq.Expressions;
using VehicleManager.Models;

namespace VehicleManager.ViewModels;

public class VehicleView
{
    public int Id { get; init; }
    public VehicleType Type { get; init; }
    public string Make { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public DateOnly? RegistrationDate { get; init; }
    public DateOnly? PurchasedDate { get; init; }
    public string OwnerName { get; init; } = string.Empty;

    public string DisplayName => $"{Make} {Model}".Trim();

    public static readonly Expression<Func<Vehicle, VehicleView>> Projection = v => new VehicleView
    {
        Id = v.Id,
        Type = v.Type,
        Make = v.Make,
        Model = v.Model,
        RegistrationNumber = v.RegistrationNumber,
        RegistrationDate = v.RegistrationDate,
        PurchasedDate = v.PurchasedDate,
        OwnerName = v.Owner!.Name
    };
}
