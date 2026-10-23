namespace VehicleManager.Services;

/// <summary>Cost figures worked out from a fill-up's raw data (never stored).</summary>
public static class FuelCalculations
{
    /// <summary>Litres in one UK (imperial) gallon.</summary>
    public const decimal LitresPerGallon = 4.54609m;

    public static decimal EffectiveCostPerLitre(decimal costPerLitre, decimal? voucherPerLitre) =>
        costPerLitre - (voucherPerLitre ?? 0);

    public static decimal TotalCost(decimal litres, decimal costPerLitre, decimal? voucherPerLitre) =>
        Math.Round(litres * EffectiveCostPerLitre(costPerLitre, voucherPerLitre), 2);
}
