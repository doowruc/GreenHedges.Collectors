namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record MotDefectBackup : BackupRow
{
    public required int MotTestId { get; init; }
    public required int Sequence { get; init; }
    public string? Type { get; init; }
    public required string Text { get; init; }
    public bool Dangerous { get; init; }
}
