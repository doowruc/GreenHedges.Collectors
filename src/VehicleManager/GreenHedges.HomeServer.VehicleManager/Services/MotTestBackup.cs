using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An MOT test from the DVSA's history (see <see cref="MotTest"/>).</summary>
public sealed record MotTestBackup : BackupRow
{
    public required int Id { get; init; }
    public required int VehicleId { get; init; }
    public required DateTime CompletedDate { get; init; }
    public bool Passed { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public int? Odometer { get; init; }
    public bool OdometerInKilometres { get; init; }
    public string? MotTestNumber { get; init; }
    public string? RegistrationAtTest { get; init; }
    public string? DataSource { get; init; }
}
