using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class MotHistoryPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Checking_the_MOT_fills_in_the_MOT_tab()
    {
        // Each pass is a year before its expiry: one 11 months ago and one 18 months ago, plus the failure in 2020.
        var today = Reminders.UkToday();
        var latestExpiry = today.AddMonths(1);
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { MotTestExpiryDates = [today.AddMonths(-6), latestExpiry] });
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db, reg)).Id;
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync($"/Vehicles/Details/{id}?tab=mot");
        Assert.Contains("class=\"tab-pane show active\" id=\"tab-mot\"", page.Html);
        Assert.Contains("No MOT tests yet.", page.Text);

        // Checking from the MOT tab comes back to it.
        var checkedPage = await client.PostFormAsync($"/Vehicles/Details/{id}?handler=Lookup", page);
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=mot"), (checkedPage.Status, checkedPage.Location));

        page = await client.GetAsync(checkedPage.Location!);
        var html = page.Html;
        Assert.Contains("data-show-period=\"all\"", html[html.IndexOf("id=\"tab-mot\"", StringComparison.Ordinal)..]);   // its own period toggle

        // Last 12 months: just the latest test. All time: all three.
        var lastYear = Period(html, "12m");
        Assert.Single(Regex.Matches(lastYear, "list-group-item"));
        Assert.Contains($"Expires {latestExpiry:dd/MM/yyyy}", lastYear);
        Assert.Contains("38,000 miles", lastYear);
        Assert.DoesNotContain(FakeVehicleApis.FailedTestDefect, lastYear);

        var allTime = Period(html, "all");
        Assert.Equal(3, Regex.Matches(allTime, "list-group-item").Count);
        Assert.Contains("Advisory 2: tyre worn close to legal limit", allTime);
        Assert.Contains(FakeVehicleApis.FailedTestDefect, allTime);
        Assert.Contains("01/01/2020", allTime);
        Assert.Single(Regex.Matches(allTime, "Tested as"));   // only the failed test, under its old number plate
        Assert.Matches(@"MOT history\s*<span class=""period-stats"" data-period=""12m"">\(1\)</span><span class=""period-stats"" data-period=""all"">\(3\)</span>", html);
    }

    /// <summary>The MOT list shown for one period of the toggle.</summary>
    private static string Period(string html, string period)
    {
        var mot = html[html.IndexOf("id=\"tab-mot\"", StringComparison.Ordinal)..];
        var start = mot.IndexOf($"<div class=\"period-stats\" data-period=\"{period}\">", StringComparison.Ordinal);
        var end = mot.IndexOf("<div class=\"period-stats\"", start + 1, StringComparison.Ordinal);
        return WebUtility.HtmlDecode(end < 0 ? mot[start..] : mot[start..end]);
    }

    [DatabaseFact]
    public async Task The_header_offers_each_check_until_its_date_is_known_and_the_MOT_tab_always_does()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db)).Id;
        var client = app.ClientFor(UserId);
        string[] Checks(string html) =>
            [.. Regex.Matches(html, @"<button type=""submit"" class=""btn ([^""]+)""><i class=""bi bi-arrow-repeat me-1""></i>Check (\w+)</button>")
                .Select(m => $"{m.Groups[2].Value}: {m.Groups[1].Value}")];

        // Not checked yet: MOT and tax links in the header, and the MOT tab's button (at the top right).
        Assert.Equal(["MOT: btn-link btn-sm py-0", "tax: btn-link btn-sm py-0", "MOT: btn-sm btn-primary"],
            Checks((await client.GetAsync($"/Vehicles/Details/{id}")).Html));

        // Each goes once its date is known.
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).MotDueDate = new DateOnly(2027, 1, 1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(["tax: btn-link btn-sm py-0", "MOT: btn-sm btn-primary"], Checks((await client.GetAsync($"/Vehicles/Details/{id}")).Html));

        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            (vehicle.MotDueDate, vehicle.TaxDueDate) = (null, new DateOnly(2027, 3, 1));
            await db.SaveChangesAsync();
        }
        var page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Equal(["MOT: btn-link btn-sm py-0", "MOT: btn-sm btn-primary"], Checks(page.Html));
        Assert.True(page.Html.IndexOf("id=\"tab-mot\"", StringComparison.Ordinal) < page.Html.IndexOf("btn-sm btn-primary\"><i class=\"bi bi-arrow-repeat", StringComparison.Ordinal));
    }

    [DatabaseFact]
    public async Task Checking_just_the_MOT_or_just_the_tax()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle
        {
            TaxDueDate = new(2027, 3, 1), DvlaMotExpiryDate = new(2026, 12, 1), MotTestExpiryDates = [new(2026, 11, 2)]
        });
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db, reg)).Id;
        var client = app.ClientFor(UserId);
        var page = await client.GetAsync($"/Vehicles/Details/{id}?tab=mot");

        // The MOT, from the DVSA alone, with the history; the page comes back to the MOT tab.
        var checkedPage = await client.PostFormAsync($"/Vehicles/Details/{id}?handler=Lookup&check=mot", page);
        Assert.Equal($"/Vehicles/Details/{id}?tab=mot", checkedPage.Location);
        Assert.Contains($"Checked {reg} with the DVSA: MOT due 02/11/2026.", (await client.GetAsync(checkedPage.Location!)).Text);
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal((new DateOnly(2026, 11, 2), (DateOnly?)null), (vehicle.MotDueDate, vehicle.TaxDueDate));
            Assert.Equal(2, await db.MotTests.CountAsync());   // the pass and the failure
        }

        // The tax, from the DVLA, leaving the MOT alone.
        checkedPage = await client.PostFormAsync($"/Vehicles/Details/{id}?handler=Lookup&check=tax", page);
        Assert.Contains($"Checked {reg} with the DVLA: tax due 01/03/2027.", (await client.GetAsync(checkedPage.Location!)).Text);
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2027, 3, 1)), (vehicle.MotDueDate, vehicle.TaxDueDate));
        }
    }

    [DatabaseFact]
    public async Task The_fuel_tab_is_the_default()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db)).Id;
        var page = await app.ClientFor(UserId).GetAsync($"/Vehicles/Details/{id}?tab=nonsense");
        Assert.Contains("class=\"tab-pane show active\" id=\"tab-fuel\"", page.Html);
        Assert.Contains("MOT <span class=\"opacity-75\">(0)</span>", page.Html);
    }
}
