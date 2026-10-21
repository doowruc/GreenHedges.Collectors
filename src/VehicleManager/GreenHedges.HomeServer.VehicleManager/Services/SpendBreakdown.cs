namespace VehicleManager.Services;

/// <summary>
/// Fuel spend per period (a month or a year, named by <see cref="Labels"/>), split by vehicle: each of
/// <see cref="Vehicles"/> has one amount per label. Vehicles with nothing spent in any of the periods are left out.
/// </summary>
public record SpendBreakdown(List<string> Labels, List<SpendBreakdown.Series> Vehicles)
{
    public record Series(int VehicleId, List<decimal> Amounts);

    public decimal Total => Vehicles.Sum(v => v.Amounts.Sum());
}
