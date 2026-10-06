using System.Net;
using System.Text.RegularExpressions;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class FuelBrandsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>Alpha@Town, Alpa@Town (a misspelt brand), Beta@Village, and an unused brand.</summary>
    private async Task<PageClient> SeedAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db);
        var day = 1;
        foreach (var stop in new[] { ("Alpha", "Town"), ("Alpa", "Town"), ("Beta", "Village") })
            await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, day++), stop);
        db.FuelBrands.Add(new() { Name = "Empty" });
        await db.SaveChangesAsync();
        return app.ClientFor(UserId);
    }

    private static List<string> ActionLinks(PageResponse page) =>
        Regex.Matches(page.Html, "<a [^>]*title=\"(?:Stations|Edit|Merge into another|Delete)\"[^>]*>").Select(m => m.Value).ToList();

    private static async Task AssertAllLinksLoadAsync(PageClient client, PageResponse page)
    {
        var links = ActionLinks(page);
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            var href = PageResponse.Href(link);
            Assert.False(href is null, $"No link on {link}");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(href!)).Status);
        }
    }

    [DatabaseFact]
    public async Task Every_fuel_brand_and_station_button_links_to_a_page_that_loads()
    {
        var client = await SeedAsync();
        var brands = await client.GetAsync("/FuelBrands");
        Assert.Contains("<title>Fuel Brands ", brands.Html);
        await AssertAllLinksLoadAsync(client, brands);

        var stations = await client.GetAsync(brands.RowLink("Alpha", "Stations")!);
        Assert.Contains("<h1 class=\"h3 mb-3\">Alpha stations</h1>", stations.Html);
        Assert.Contains("<td>Town</td>", stations.Html);
        Assert.DoesNotContain("<td>Village</td>", stations.Html);   // Beta's
        await AssertAllLinksLoadAsync(client, stations);

        var edit = await client.GetAsync(stations.RowLink("Town", "Edit")!);
        Assert.Matches(@"<a href=""/FuelBrands"">Fuel Brands</a>.*<a href=""/FuelBrands/\d+/Stations"">Alpha</a>.*Edit station", Regex.Replace(edit.Html, @"\s+", " "));
    }

    [DatabaseFact]
    public async Task Stations_are_no_longer_a_top_level_page()
    {
        var client = await SeedAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Stations")).Status);
    }

    [DatabaseFact]
    public async Task A_station_cant_be_reached_through_another_brands_url()
    {
        var client = await SeedAsync();
        var brands = await client.GetAsync("/FuelBrands");
        var betaStations = brands.RowLink("Beta", "Stations")!;
        var villageEdit = (await client.GetAsync(betaStations)).RowLink("Village", "Edit")!;
        var alphaStations = brands.RowLink("Alpha", "Stations")!;

        var wrongBrand = villageEdit.Replace(betaStations, alphaStations);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(wrongBrand)).Status);
    }

    [DatabaseFact]
    public async Task Renaming_a_fuel_brand()
    {
        var client = await SeedAsync();
        var edit = (await client.GetAsync("/FuelBrands")).RowLink("Alpa", "Edit")!;
        var form = await client.GetAsync(edit);
        Assert.Contains("<h1 class=\"h3 mb-3\">Edit fuel brand</h1>", form.Html);

        var clash = await client.PostFormAsync(edit, form, new() { ["Place.Name"] = "alpha" });
        Assert.Equal(HttpStatusCode.OK, clash.Status);
        Assert.Contains("already a fuel brand", clash.Errors);

        var saved = await client.PostFormAsync(edit, form, new() { ["Place.Name"] = "Alpa Fuel" });
        Assert.Equal((HttpStatusCode.Redirect, "/FuelBrands"), (saved.Status, saved.Location));
        Assert.Contains("<td>Alpa Fuel</td>", (await client.GetAsync("/FuelBrands")).Html);
    }

    [DatabaseFact]
    public async Task Merging_a_fuel_brand_combines_its_stations()
    {
        var client = await SeedAsync();
        var merge = (await client.GetAsync("/FuelBrands")).RowLink("Alpa", "Merge")!;
        var form = await client.GetAsync(merge);

        var merged = await client.PostFormAsync(merge, form, new() { ["IntoId"] = form.OptionValue("Alpha") });
        Assert.Equal((HttpStatusCode.Redirect, "/FuelBrands"), (merged.Status, merged.Location));
        var brands = await client.GetAsync("/FuelBrands");
        Assert.DoesNotContain("<td>Alpa</td>", brands.Html);
        Assert.Matches(@"<td>Town</td>\s*<td class=""text-end"">2</td>", (await client.GetAsync(brands.RowLink("Alpha", "Stations")!)).Html);
    }

    [DatabaseFact]
    public async Task Deleting_an_unused_fuel_brand()
    {
        var client = await SeedAsync();
        var delete = (await client.GetAsync("/FuelBrands")).RowLink("Empty", "Delete")!;
        var deleted = await client.PostFormAsync(delete, await client.GetAsync(delete));
        Assert.Equal((HttpStatusCode.Redirect, "/FuelBrands"), (deleted.Status, deleted.Location));
        Assert.DoesNotContain("<td>Empty</td>", (await client.GetAsync("/FuelBrands")).Html);
    }

    [DatabaseFact]
    public async Task Moving_a_station_to_another_brand_returns_to_that_brands_stations()
    {
        var client = await SeedAsync();
        var brands = await client.GetAsync("/FuelBrands");
        var edit = (await client.GetAsync(brands.RowLink("Beta", "Stations")!)).RowLink("Village", "Edit")!;
        var form = await client.GetAsync(edit);

        var saved = await client.PostFormAsync(edit, form, new() { ["Place.BrandId"] = form.OptionValue("Alpha") });
        Assert.Equal((HttpStatusCode.Redirect, brands.RowLink("Alpha", "Stations")), (saved.Status, saved.Location));
        Assert.Contains("<td>Village</td>", (await client.GetAsync(saved.Location!)).Html);
    }

    [DatabaseFact]
    public async Task Merging_a_station_into_another_brands_station()
    {
        var client = await SeedAsync();
        var brands = await client.GetAsync("/FuelBrands");
        var betaStations = brands.RowLink("Beta", "Stations")!;
        var merge = (await client.GetAsync(betaStations)).RowLink("Village", "Merge")!;
        var form = await client.GetAsync(merge);

        var merged = await client.PostFormAsync(merge, form, new() { ["IntoId"] = form.OptionValue("Town (Alpha)") });
        Assert.Equal((HttpStatusCode.Redirect, betaStations), (merged.Status, merged.Location));
        Assert.Matches(@"<td>Town</td>\s*<td class=""text-end"">2</td>", (await client.GetAsync(brands.RowLink("Alpha", "Stations")!)).Html);
    }

    [DatabaseFact]
    public async Task Only_an_unused_station_can_be_deleted()
    {
        var client = await SeedAsync();
        await using (var db = Db())
        {
            var beta = db.FuelBrands.Single(b => b.Name == "Beta");
            db.FuelStations.Add(new() { FuelBrandId = beta.Id, Name = "Spare" });   // no fill-ups
            await db.SaveChangesAsync();
        }
        var betaStations = (await client.GetAsync("/FuelBrands")).RowLink("Beta", "Stations")!;
        var stations = await client.GetAsync(betaStations);

        var spare = stations.RowLink("Spare", "Delete")!;
        var deleted = await client.PostFormAsync(spare, await client.GetAsync(spare));
        Assert.Equal((HttpStatusCode.Redirect, betaStations), (deleted.Status, deleted.Location));
        Assert.DoesNotContain("<td>Spare</td>", (await client.GetAsync(betaStations)).Html);

        var village = stations.RowLink("Village", "Delete")!;
        var form = await client.GetAsync(village);
        Assert.Contains("can't be deleted because 1 fill-up(s) use it", form.Text);
        var refused = await client.PostFormAsync(village, form);
        Assert.Equal((HttpStatusCode.Redirect, village), (refused.Status, refused.Location));
        Assert.Contains("<td>Village</td>", (await client.GetAsync(betaStations)).Html);
    }
}
