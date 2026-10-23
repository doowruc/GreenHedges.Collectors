using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class FuelStopInputTests
{
    private static string[] Pairs(string? brands, string? stations) =>
        FuelStopInput.FromText(brands, stations).Select(s => $"{s.Brand}@{s.Station}").ToArray();

    [Fact]
    public void Single_brand_and_station() =>
        Assert.Equal(["Shell@Meadowbank"], Pairs("Shell", " Meadowbank "));

    [Fact]
    public void Split_fill_up_pairs_brands_and_stations_by_position() =>
        Assert.Equal(["BP@Oak Lane", "Sainsbury's@Hill Top"], Pairs("BP+Sainsbury's", "Oak Lane + Hill Top"));

    [Fact]
    public void Uneven_split_pads_the_shorter_side() =>
        Assert.Equal(["Shell@Meadowbank", "BP@"], Pairs("Shell + BP", "Meadowbank"));

    [Fact]
    public void Blank_is_no_stops() =>
        Assert.Empty(Pairs(null, "  "));
}
