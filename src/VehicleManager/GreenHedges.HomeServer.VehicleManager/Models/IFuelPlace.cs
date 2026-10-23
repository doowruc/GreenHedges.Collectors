namespace VehicleManager.Models;

/// <summary>A named place on a fill-up: a <see cref="FuelBrand"/> or a <see cref="FuelStation"/>.</summary>
public interface IFuelPlace
{
    int Id { get; }
    string Name { get; set; }
}
