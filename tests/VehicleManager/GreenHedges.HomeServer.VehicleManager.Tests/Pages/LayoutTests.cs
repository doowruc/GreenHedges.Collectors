using System.Net;
using System.Text.RegularExpressions;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class LayoutTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Manage_menu_holds_the_list_pages_and_each_link_loads()
    {
        var client = app.ClientFor(UserId);
        var page = await client.GetAsync("/");
        var menu = Regex.Match(page.Html, @"dropdown-toggle[^>]*>Manage</button>\s*<ul class=""dropdown-menu"">(.*?)</ul>", RegexOptions.Singleline);
        Assert.True(menu.Success, "No Manage menu");

        var items = Regex.Matches(menu.Groups[1].Value, "<a [^>]*>([^<]+)</a>").Select(m => (Text: m.Groups[1].Value, Href: PageResponse.Href(m.Value))).ToList();
        Assert.Equal(["Vehicles", "Owners", "Fuel Brands", "Import", "Backup", "Restore"], items.Select(i => i.Text));
        Assert.Equal("/FuelBrands", items.Single(i => i.Text == "Fuel Brands").Href);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Brands")).Status);   // the old address is gone
        foreach (var (text, href) in items)
            Assert.True(href is not null && (await client.GetAsync(href)).Status == HttpStatusCode.OK, $"{text} ({href}) doesn't load");

        // Only Dashboard and Fuel log stay as top-level links.
        var topLevel = Regex.Matches(page.Html, @"<a class=""nav-link""[^>]*>([^<]+)</a>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Dashboard", "Fuel Log"], topLevel);
        Assert.Matches(@"<a class=""nav-link"" href=""/FuelLog"">Fuel Log</a>", page.Html);
    }

    [DatabaseFact]
    public async Task Table_column_headers_are_title_case()
    {
        int vehicleId;
        await using (var db = Db())
        {
            vehicleId = (await TestData.AddVehicleAsync(db)).Id;
            await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
        }
        var client = app.ClientFor(UserId);
        var brands = await client.GetAsync("/FuelBrands");
        var pages = new[] { "/FuelLog", $"/Vehicles/Details/{vehicleId}", "/Vehicles", "/Owners", "/FuelBrands", brands.RowLink("Shell", "Stations")! };

        foreach (var url in pages)
        {
            var headers = Regex.Matches((await client.GetAsync(url)).Text, "<th[^>]*>([^<]+)</th>").Select(m => m.Groups[1].Value.Trim()).ToList();
            Assert.True(headers.Count > 0, $"No table on {url}");
            foreach (var header in headers)
            {
                // Every word capitalised, including after "-" and "/" ("Fill-Ups", "£/Litre"); "p" (pence) is a unit.
                var words = Regex.Split(header, @"[\s/-]+").Where(w => w.Length > 0 && char.IsLetter(w[0]) && w != "p");
                Assert.True(words.All(w => char.IsUpper(w[0])), $"\"{header}\" on {url} isn't title case");
            }
        }
    }
}
