namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record FuelEntryStopBackup : BackupRow
{
    public required int FuelEntryId { get; init; }
    public required int Sequence { get; init; }
    public required int FuelStationId { get; init; }
}
