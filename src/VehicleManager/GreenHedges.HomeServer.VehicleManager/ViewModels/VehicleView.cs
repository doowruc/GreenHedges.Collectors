using System.Linq.Expressions;
using VehicleManager.Models;

namespace VehicleManager.ViewModels;

public class VehicleView
{
    public int Id { get; init; }
    public VehicleType Type { get; init; }
    public FuelType FuelType { get; init; }
    public string Make { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public DateOnly? RegistrationDate { get; init; }
    public DateOnly? PurchasedDate { get; init; }
    public DateOnly? MotDueDate { get; init; }
    public DateOnly? TaxDueDate { get; init; }
    public DateOnly? InsuranceDueDate { get; init; }
    public DateOnly? ServiceDueDate { get; init; }
    public string OwnerName { get; init; } = string.Empty;

    public string DisplayName => $"{Make} {Model}".Trim();

    /// <summary>The due dates that are set, soonest first, as of <paramref name="today"/>.</summary>
    public List<DueDateView> DueDates(DateOnly today) => DueDateView.For(this, today);

    public static readonly Expression<Func<Vehicle, VehicleView>> Projection = v => new VehicleView
    {
        Id = v.Id,
        Type = v.Type,
        FuelType = v.FuelType,
        Make = v.Make,
        Model = v.Model,
        RegistrationNumber = v.RegistrationNumber,
        RegistrationDate = v.RegistrationDate,
        PurchasedDate = v.PurchasedDate,
        MotDueDate = v.MotDueDate,
        TaxDueDate = v.TaxDueDate,
        InsuranceDueDate = v.InsuranceDueDate,
        ServiceDueDate = v.ServiceDueDate,
        OwnerName = v.Owner!.Name
    };
}
