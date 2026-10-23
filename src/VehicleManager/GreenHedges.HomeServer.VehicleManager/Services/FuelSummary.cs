namespace GreenHedges.HomeServer.VehicleManager.Services;

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
