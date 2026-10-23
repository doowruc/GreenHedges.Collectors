namespace VehicleManager.ViewModels;

/// <summary>One stop of a fill-up; the ids are for linking to the station's page.</summary>
public record FuelStopView(int BrandId, string Brand, int StationId, string Station);
