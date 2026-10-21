using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Tests.Unit;

public class SpendStatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    /// <summary>A fill-up costing £<paramref name="cost"/> (10 litres).</summary>
    private static FuelEntryView Fill(int vehicleId, DateOnly date, decimal cost, decimal? voucher = null) =>
        new() { VehicleId = vehicleId, Date = date, Litres = 10, CostPerLitre = cost / 10, VoucherPerLitre = voucher };

    [Fact]
    public void Monthly_covers_the_12_months_up_to_this_one_by_vehicle()
    {
        var spend = SpendStats.Monthly([
            Fill(1, new(2025, 10, 31), 99),   // before the 12 months
            Fill(1, new(2025, 11, 1), 50),
            Fill(1, new(2025, 11, 20), 25),
            Fill(2, new(2026, 3, 5), 40),
            Fill(1, Today, 60),
            Fill(1, Today.AddDays(1), 99)     // after today
        ], [2, 1, 3], Today);

        Assert.Equal(12, spend.Labels.Count);
        Assert.Equal(("Nov 25", "Oct 26"), (spend.Labels[0], spend.Labels[^1]));
        // In the order given; vehicle 3 has nothing in the period.
        Assert.Equal([2, 1], spend.Vehicles.Select(v => v.VehicleId));
        Assert.Equal([75m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 60m], spend.Vehicles[1].Amounts);
        Assert.Equal(40m, spend.Vehicles[0].Amounts[4]);
        Assert.Equal(175m, spend.Total);
    }

    [Fact]
    public void Costs_are_after_vouchers()
    {
        // £1.50 a litre less 5p voucher, 10 litres.
        var spend = SpendStats.Monthly([Fill(1, Today, 15, voucher: 0.05m)], [1], Today);
        Assert.Equal(14.50m, spend.Total);
    }

    [Fact]
    public void Yearly_runs_from_the_first_fill_up_to_this_year_including_empty_years()
    {
        var spend = SpendStats.Yearly([Fill(1, new(2023, 6, 1), 30), Fill(1, new(2023, 7, 1), 20), Fill(1, new(2026, 1, 1), 10)], [1], Today);

        Assert.Equal(["2023", "2024", "2025", "2026"], spend.Labels);
        Assert.Equal([50m, 0, 0, 10m], spend.Vehicles.Single().Amounts);
    }

    [Fact]
    public void No_fill_ups_give_nothing()
    {
        Assert.Empty(SpendStats.Yearly([], [1], Today).Labels);
        Assert.Empty(SpendStats.Years([], Today));
        Assert.Empty(SpendStats.Monthly([], [1], Today).Vehicles);
    }

    [Fact]
    public void Years_have_running_totals_by_month_newest_first()
    {
        var years = SpendStats.Years([
            Fill(1, new(2025, 1, 10), 40),
            Fill(2, new(2025, 3, 10), 60),
            Fill(1, new(2025, 12, 31), 100),
            Fill(1, new(2026, 2, 1), 30)
        ], Today);

        Assert.Equal([2026, 2025], years.Select(y => y.Year));
        Assert.Equal([40m, 40, 100, 100, 100, 100, 100, 100, 100, 100, 100, 200], years[1].Cumulative);
        Assert.Equal((200m, 3), (years[1].Total, years[1].FillUps));
        // This year stops at this month.
        Assert.Equal([0m, 30, 30, 30, 30, 30, 30, 30, 30, 30, null, null], years[0].Cumulative);
    }

    [Fact]
    public void Change_is_against_the_year_before_and_this_year_against_last_year_to_the_same_day()
    {
        var years = SpendStats.Years([
            Fill(1, new(2024, 5, 1), 100),
            Fill(1, new(2025, 5, 1), 150),
            Fill(1, new(2025, 10, 22), 500),  // after today's date last year, so not compared
            Fill(1, new(2026, 5, 1), 120)
        ], Today);

        var (now, last, first) = (years[0], years[1], years[2]);
        Assert.Equal((-0.2m, Today), (now.Change, now.ToDate));
        Assert.Equal((5.5m, (DateOnly?)null), (last.Change, last.ToDate));
        Assert.Null(first.Change);
    }

    [Fact]
    public void No_change_after_a_year_with_nothing_spent()
    {
        var years = SpendStats.Years([Fill(1, new(2024, 5, 1), 100), Fill(1, new(2026, 5, 1), 50)], Today);
        // 2026 after an empty 2025; 2025 is all down on 2024.
        Assert.Equal([null, -1m, null], years.Select(y => y.Change));
    }
}
