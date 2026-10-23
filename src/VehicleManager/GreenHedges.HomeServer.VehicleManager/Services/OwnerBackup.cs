namespace VehicleManager.Services;

public sealed record OwnerBackup : BackupRow
{
    public required int Id { get; init; }
    public required string Name { get; set; }
}
