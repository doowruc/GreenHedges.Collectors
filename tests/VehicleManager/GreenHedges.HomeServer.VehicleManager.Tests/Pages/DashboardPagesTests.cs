using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class DashboardPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    private static JsonElement Attribute(string html, string name) =>
        JsonDocument.Parse(WebUtility.HtmlDecode(Regex.Match(html, $@"{name}=""([^""]*)""").Groups[1].Value)).RootElement;

    [DatabaseFact]
    public async Task The_spending_card_charts_fuel_spend_by_month_year_and_year_on_year()
    {
        await using (var db = Db())
        {
            var id = (await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration())).Id;
            // £60 each (40 litres at £1.50): one today, one a year ago today, one three years ago.
            foreach (var date in new[] { Today, Today.AddYears(-1), Today.AddYears(-3) })
                await TestData.AddFillUpAsync(db, id, date, ("Shell", "Meadowbank"));
        }

        var page = await app.ClientFor(UserId).GetAsync("/");
        Assert.Contains("Fuel spending", page.Text);
        Assert.Contains("chart.js", page.Html);

        // The last 12 months: a year ago today is the month before the first one.
        var monthly = Attribute(page.Html, "data-spend-chart");
        Assert.Equal(Today.ToString("MMM yy"), monthly.GetProperty("labels")[11].GetString());
        var amounts = monthly.GetProperty("vehicles")[0].GetProperty("amounts").EnumerateArray().Select(a => a.GetDecimal()).ToList();
        Assert.Equal([0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 60m], amounts);
        Assert.Equal("Test Car", monthly.GetProperty("vehicles")[0].GetProperty("name").GetString());

        var trend = Attribute(page.Html, "data-spend-trend");
        Assert.Equal([Today.Year, Today.Year - 1, Today.Year - 2, Today.Year - 3], trend.EnumerateArray().Select(y => y.GetProperty("year").GetInt32()));

        // This year is level with last year to the same day; the empty year has no change after it.
        var rows = Regex.Matches(page.Text, @"<tr>\s*<td>(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ")), @"\s+", " ").Trim())
            .ToList();
        Assert.Equal([
            $"{Today.Year} to {Today:d MMM} £60 0% 1",
            $"{Today.Year - 1} £60 – 1",
            $"{Today.Year - 2} £0 −100% 0",
            $"{Today.Year - 3} £60 – 1"
        ], rows);
    }

    [DatabaseFact]
    public async Task No_fill_ups_means_no_spending_card()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration());

        var page = await app.ClientFor(UserId).GetAsync("/");
        Assert.DoesNotContain("Fuel spending", page.Text);
        Assert.DoesNotContain("chart.js", page.Html);
    }
}
