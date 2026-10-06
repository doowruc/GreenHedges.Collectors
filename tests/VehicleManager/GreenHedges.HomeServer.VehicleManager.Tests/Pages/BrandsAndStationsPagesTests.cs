using System.Net;
using System.Text.RegularExpressions;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class BrandsAndStationsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
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

    [DatabaseTheory]
    [InlineData("/Brands")]
    [InlineData("/Stations")]
    public async Task Every_edit_merge_and_delete_button_links_to_a_page_that_loads(string list)
    {
        var client = await SeedAsync();
        var page = await client.GetAsync(list);
        Assert.Equal(HttpStatusCode.OK, page.Status);

        var buttons = Regex.Matches(page.Html, "<a [^>]*title=\"(?:Edit|Merge into another|Delete)\"[^>]*>").Select(m => m.Value).ToList();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            var href = PageResponse.Href(button);
            Assert.False(href is null, $"No link on {button}");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(href!)).Status);
        }
    }

    [DatabaseFact]
    public async Task Renaming_a_brand()
    {
        var client = await SeedAsync();
        var edit = (await client.GetAsync("/Brands")).RowLink("Alpa", "Edit")!;
        var form = await client.GetAsync(edit);

        var clash = await client.PostFormAsync(edit, form, new() { ["Place.Name"] = "alpha" });
        Assert.Equal(HttpStatusCode.OK, clash.Status);
        Assert.Contains("already a brand", clash.Errors);

        var saved = await client.PostFormAsync(edit, form, new() { ["Place.Name"] = "Alpa Fuel" });
        Assert.Equal((HttpStatusCode.Redirect, "/brands"), (saved.Status, saved.Location));
        Assert.Contains("<td>Alpa Fuel</td>", (await client.GetAsync("/Brands")).Html);
    }

    [DatabaseFact]
    public async Task Merging_a_brand_combines_its_stations()
    {
        var client = await SeedAsync();
        var merge = (await client.GetAsync("/Brands")).RowLink("Alpa", "Merge")!;
        var form = await client.GetAsync(merge);

        var merged = await client.PostFormAsync(merge, form, new() { ["IntoId"] = form.OptionValue("Alpha") });
        Assert.Equal((HttpStatusCode.Redirect, "/brands"), (merged.Status, merged.Location));
        Assert.Matches(@"<td>Town</td>\s*<td>Alpha</td>\s*<td class=""text-end"">2</td>", (await client.GetAsync("/Stations")).Html);
    }

    [DatabaseFact]
    public async Task Deleting_an_unused_brand()
    {
        var client = await SeedAsync();
        var delete = (await client.GetAsync("/Brands")).RowLink("Empty", "Delete")!;
        var deleted = await client.PostFormAsync(delete, await client.GetAsync(delete));
        Assert.Equal((HttpStatusCode.Redirect, "/brands"), (deleted.Status, deleted.Location));
        Assert.DoesNotContain("<td>Empty</td>", (await client.GetAsync("/Brands")).Html);
    }

    [DatabaseFact]
    public async Task Changing_a_stations_brand()
    {
        var client = await SeedAsync();
        var edit = (await client.GetAsync("/Stations")).RowLink("Village", "Edit")!;
        var form = await client.GetAsync(edit);
        Assert.Contains("name=\"Place.BrandId\"", form.Html);

        var saved = await client.PostFormAsync(edit, form, new() { ["Place.BrandId"] = form.OptionValue("Alpha") });
        Assert.Equal((HttpStatusCode.Redirect, "/stations"), (saved.Status, saved.Location));
        Assert.Matches(@"<td>Village</td>\s*<td>Alpha</td>", (await client.GetAsync("/Stations")).Html);
    }
}
