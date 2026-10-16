namespace VehicleManager.Services;

public sealed record FuelEntryBackup : BackupRow
{
    public required int Id { get; init; }
    public required int VehicleId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Litres { get; init; }
    public required decimal CostPerLitre { get; init; }
    public decimal? VoucherPerLitre { get; init; }
    public decimal? TripAverageMpg { get; init; }
    public decimal? TripMiles { get; init; }
    public int? Range { get; init; }
    public int? Odometer { get; init; }
    public string? Notes { get; init; }
}
