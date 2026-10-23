namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

/// <param name="BrandId">A station's brand; null for brands.</param>
/// <param name="Brand">A station's brand name; null for brands.</param>
/// <param name="Stations">A brand's number of stations; null for stations.</param>
public record FuelPlaceRow(int Id, string Name, int? BrandId, string? Brand, int FillUps, int? Stations)
{
    /// <summary>Name to show in a list mixing several brands' stations, e.g. "Riverside (Asda)".</summary>
    public string FullName => Brand is null ? Name : $"{Name} ({Brand})";

    public bool InUse => FillUps > 0 || Stations > 0;
}
