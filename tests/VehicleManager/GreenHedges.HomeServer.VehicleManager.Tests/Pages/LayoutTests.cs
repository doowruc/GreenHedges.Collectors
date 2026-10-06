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
        Assert.Equal(["Vehicles", "Owners", "Fuel Brands", "Import"], items.Select(i => i.Text));
        Assert.Equal("/FuelBrands", items.Single(i => i.Text == "Fuel Brands").Href);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Brands")).Status);   // the old address is gone
        foreach (var (text, href) in items)
            Assert.True(href is not null && (await client.GetAsync(href)).Status == HttpStatusCode.OK, $"{text} ({href}) doesn't load");

        // Only Dashboard and Fuel log stay as top-level links.
        var topLevel = Regex.Matches(page.Html, @"<a class=""nav-link""[^>]*>([^<]+)</a>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Dashboard", "Fuel log"], topLevel);
    }
}
