using VehicleManager.ViewModels;

namespace VehicleManager.Services;

public record FuelSummary(
    int FillUps,
    decimal TotalLitres,
    decimal TotalCost,
    decimal? TotalMiles,
    decimal? AverageMpg,
    decimal? AverageTripComputerMpg,
    decimal? AverageCostPerLitre,
    decimal? CostPerMile,
    int? LatestOdometer,
    DateOnly? FirstFillUp,
    DateOnly? LastFillUp)
{
    public static readonly FuelSummary Empty = new(0, 0, 0, null, null, null, null, null, null, null, null);
}

public class FuelStatsService
{
    /// <summary>
    /// Summarises a set of fill-ups. MPG is weighted (total miles / total gallons)
    /// over entries that have trip miles recorded, rather than an average of averages.
    /// </summary>
    public FuelSummary Summarise(IEnumerable<FuelEntryView> source, DateOnly? from = null)
    {
        var entries = source.Where(e => from is null || e.Date >= from).ToList();
        if (entries.Count == 0) return FuelSummary.Empty;

        var totalLitres = entries.Sum(e => e.Litres);
        var totalCost = entries.Sum(e => e.TotalCost);

        var withMiles = entries.Where(e => e.TripMiles is > 0).ToList();
        decimal? totalMiles = withMiles.Count > 0 ? withMiles.Sum(e => e.TripMiles!.Value) : null;
        decimal? avgMpg = null, costPerMile = null;
        if (totalMiles is > 0)
        {
            var gallons = withMiles.Sum(e => e.Gallons);
            avgMpg = gallons > 0 ? Math.Round(totalMiles.Value / gallons, 1) : null;
            costPerMile = Math.Round(withMiles.Sum(e => e.TotalCost) / totalMiles.Value, 3);
        }

        var dash = entries.Where(e => e.TripAverageMpg is > 0).Select(e => e.TripAverageMpg!.Value).ToList();

        return new FuelSummary(
            FillUps: entries.Count,
            TotalLitres: totalLitres,
            TotalCost: totalCost,
            TotalMiles: totalMiles,
            AverageMpg: avgMpg,
            AverageTripComputerMpg: dash.Count > 0 ? Math.Round(dash.Average(), 1) : null,
            AverageCostPerLitre: totalLitres > 0 ? Math.Round(totalCost / totalLitres, 3) : null,
            CostPerMile: costPerMile,
            LatestOdometer: entries.Where(e => e.Odometer.HasValue).Select(e => e.Odometer).Max(),
            FirstFillUp: entries.Min(e => e.Date),
            LastFillUp: entries.Max(e => e.Date));
    }
}
