using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Tests.Unit;

public class FuelStatsServiceTests
{
    private static FuelEntryView Fill(int day, decimal litres, decimal? miles, int? odometer = null) =>
        new() { Date = new DateOnly(2026, 1, day), Litres = litres, CostPerLitre = 1.5m, TripMiles = miles, Odometer = odometer };

    [Fact]
    public void Average_mpg_is_weighted_by_fuel_not_an_average_of_averages()
    {
        // 45.5 and 22.7 MPG tanks: average of averages would be 34.1; weighted is 500 miles / 13.2 gallons.
        var summary = new FuelStatsService().Summarise([Fill(1, 40, 400), Fill(2, 20, 100)]);
        Assert.Equal(37.9m, summary.AverageMpg);
        Assert.Equal(500m, summary.TotalMiles);
        Assert.Equal(90.00m, summary.TotalCost);
    }

    [Fact]
    public void Fill_ups_without_miles_count_towards_cost_but_not_mpg()
    {
        var summary = new FuelStatsService().Summarise([Fill(1, 40, 400), Fill(2, 30, null)]);
        Assert.Equal(45.5m, summary.AverageMpg);
        Assert.Equal(70m, summary.TotalLitres);
        Assert.Equal(2, summary.FillUps);
    }

    [Fact]
    public void From_date_limits_the_period()
    {
        var summary = new FuelStatsService().Summarise([Fill(1, 40, 400, 1000), Fill(10, 20, 100, 1100)], from: new DateOnly(2026, 1, 5));
        Assert.Equal(1, summary.FillUps);
        Assert.Equal(1100, summary.LatestOdometer);
    }

    [Fact]
    public void No_fill_ups_is_empty()
    {
        Assert.Equal(FuelSummary.Empty, new FuelStatsService().Summarise([]));
    }
}
