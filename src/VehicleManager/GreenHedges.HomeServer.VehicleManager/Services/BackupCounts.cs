namespace VehicleManager.Services;

public sealed record BackupCounts(int Owners, int Vehicles, int FuelBrands, int FuelStations, int FillUps, int Services)
{
    public override string ToString() =>
        $"{Owners} owner(s), {Vehicles} vehicle(s), {FuelBrands} fuel brand(s), {FuelStations} station(s), {FillUps} fill-up(s) and {Services} service(s)";
}
