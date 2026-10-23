using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// Fuel spend over time for the dashboard's spending chart: by month or year and vehicle, and year on year.
/// Costs are each fill-up's <see cref="FuelEntryView.TotalCost"/> (after any voucher). Pass "today" as
/// <see cref="Reminders.UkToday"/>.
/// </summary>
public static class SpendStats
{
    /// <summary>The 12 calendar months up to and including today's, oldest first ("Nov 25").</summary>
    public static SpendBreakdown Monthly(IEnumerable<FuelEntryView> entries, IEnumerable<int> vehicleIds, DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var months = Enumerable.Range(0, 12).Select(first.AddMonths).ToList();
        return Breakdown(entries.Where(e => e.Date >= first && e.Date <= today), vehicleIds,
            months.Select(m => m.ToString("MMM yy")).ToList(),
            e => (e.Date.Year - first.Year) * 12 + e.Date.Month - first.Month);
    }

    /// <summary>Every calendar year from the first fill-up's to today's, oldest first.</summary>
    public static SpendBreakdown Yearly(IEnumerable<FuelEntryView> entries, IEnumerable<int> vehicleIds, DateOnly today)
    {
        var list = entries.Where(e => e.Date <= today).ToList();
        if (list.Count == 0) return new SpendBreakdown([], []);
        var firstYear = list.Min(e => e.Date.Year);
        return Breakdown(list, vehicleIds,
            Enumerable.Range(firstYear, today.Year - firstYear + 1).Select(y => y.ToString()).ToList(),
            e => e.Date.Year - firstYear);
    }

    /// <summary>Every calendar year from the first fill-up's to today's, newest first.</summary>
    public static List<YearSpend> Years(IEnumerable<FuelEntryView> entries, DateOnly today)
    {
        var list = entries.Where(e => e.Date <= today).ToList();
        if (list.Count == 0) return [];
        var firstYear = list.Min(e => e.Date.Year);
        var byYear = list.ToLookup(e => e.Date.Year);
        // AddYears keeps 29 February as 28 February.
        var sameDayLastYear = today.AddYears(-1);

        return Enumerable.Range(firstYear, today.Year - firstYear + 1).Reverse().Select(year =>
        {
            var current = year == today.Year;
            var total = byYear[year].Sum(e => e.TotalCost);
            // The current year only runs to today, so compare it with last year up to the same day.
            var before = year == firstYear ? (decimal?)null
                : byYear[year - 1].Where(e => !current || e.Date <= sameDayLastYear).Sum(e => e.TotalCost);

            decimal running = 0;
            var cumulative = Enumerable.Range(1, 12).Select(month =>
            {
                if (current && month > today.Month) return (decimal?)null;
                running += byYear[year].Where(e => e.Date.Month == month).Sum(e => e.TotalCost);
                return running;
            }).ToList();

            return new YearSpend(year, total, byYear[year].Count(),
                before is > 0 ? Math.Round((total - before.Value) / before.Value, 3) : null,
                current ? today : null,
                cumulative);
        }).ToList();
    }

    /// <summary>Totals per vehicle (in <paramref name="vehicleIds"/>' order) in each of the labelled periods.</summary>
    private static SpendBreakdown Breakdown(IEnumerable<FuelEntryView> entries, IEnumerable<int> vehicleIds, List<string> labels, Func<FuelEntryView, int> period)
    {
        var byVehicle = entries.ToLookup(e => e.VehicleId);
        var series = vehicleIds.Where(id => byVehicle[id].Any()).Select(id =>
        {
            var amounts = new decimal[labels.Count];
            foreach (var e in byVehicle[id]) amounts[period(e)] += e.TotalCost;
            return new SpendBreakdown.Series(id, amounts.ToList());
        }).ToList();
        return new SpendBreakdown(labels, series);
    }
}
