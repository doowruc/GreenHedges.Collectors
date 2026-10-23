namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record FuelStationBackup : BackupRow
{
    public required int Id { get; init; }
    public required int FuelBrandId { get; init; }
    public required string Name { get; set; }
}
