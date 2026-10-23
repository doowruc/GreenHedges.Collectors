using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class FuelEntryViewTests
{
    private static FuelEntryView Entry(decimal litres = 40, decimal price = 1.5m, decimal? voucher = 0.05m, decimal? miles = 400, decimal? dash = 48) =>
        new() { Litres = litres, CostPerLitre = price, VoucherPerLitre = voucher, TripMiles = miles, TripAverageMpg = dash };

    [Fact]
    public void Costs_take_the_voucher_off_the_price()
    {
        var e = Entry();
        Assert.Equal(1.45m, e.EffectiveCostPerLitre);
        Assert.Equal(58.00m, e.TotalCost);
    }

    [Fact]
    public void Real_mpg_uses_uk_gallons()
    {
        // 400 miles on 40 litres = 400 / (40 / 4.54609) = 45.46
        var e = Entry();
        Assert.Equal(45.5m, e.CalculatedMpg);
        Assert.Equal(2.5m, e.MpgDifference);   // dash 48 vs real 45.5: optimistic
        Assert.Equal(0.145m, e.CostPerMile);   // £58 / 400 miles
    }

    [Fact]
    public void Figures_needing_trip_miles_are_blank_without_them()
    {
        var e = Entry(miles: null);
        Assert.Null(e.CalculatedMpg);
        Assert.Null(e.MpgDifference);
        Assert.Null(e.CostPerMile);
    }

    [Fact]
    public void Split_fill_up_shows_brands_and_stations_in_order()
    {
        var e = new FuelEntryView { Stops = [new(1, "BP", 1, "Oak Lane"), new(2, "Sainsbury's", 2, "Hill Top")] };
        Assert.Equal("BP + Sainsbury's", e.Brand);
        Assert.Equal("Oak Lane + Hill Top", e.Station);
        Assert.Equal("BP Oak Lane + Sainsbury's Hill Top", e.FuelStation);
    }

    [Fact]
    public void Single_stop_fuel_station_is_brand_then_station() =>
        Assert.Equal("Shell Meadowbank", new FuelEntryView { Stops = [new(1, "Shell", 1, "Meadowbank")] }.FuelStation);
}
