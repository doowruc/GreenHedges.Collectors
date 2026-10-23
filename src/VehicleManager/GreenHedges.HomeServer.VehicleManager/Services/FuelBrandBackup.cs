namespace VehicleManager.Services;

public sealed record FuelBrandBackup : BackupRow
{
    public required int Id { get; init; }
    public required string Name { get; set; }
}
