using System.Net;
using System.Text.RegularExpressions;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class FuelPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task<(PageClient Client, int VehicleId)> SeedAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db);
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 1), ("Beta", "Village"));
        return (app.ClientFor(UserId), vehicle.Id);
    }

    private static Dictionary<string, string> FillUp(int vehicleId, params (string Brand, string Station)[] stops)
    {
        var fields = new Dictionary<string, string>
        {
            ["Entry.VehicleId"] = vehicleId.ToString(), ["Entry.Date"] = "2026-01-05",
            ["Entry.Litres"] = "30", ["Entry.CostPerLitre"] = "1.4"
        };
        for (var i = 0; i < stops.Length; i++)
            (fields[$"Entry.Stops[{i}].Brand"], fields[$"Entry.Stops[{i}].Station"]) = stops[i];
        return fields;
    }

    [DatabaseFact]
    public async Task Add_fill_up_form_prefills_the_last_station_and_suggests_stations_with_brands()
    {
        var (client, _) = await SeedAsync();
        var form = await client.GetAsync("/Fuel/Create");
        Assert.Equal(HttpStatusCode.OK, form.Status);
        Assert.Contains("name=\"Entry.Stops[0].Station\"", form.Html);
        Assert.Matches("name=\"Entry.Stops\\[0\\].Station\"[^>]*value=\"Village\"", form.Html);
        Assert.Contains("<option value=\"Village\" label=\"Beta\">", form.Html);
    }

    [DatabaseFact]
    public async Task A_brand_without_a_station_is_rejected()
    {
        var (client, vehicleId) = await SeedAsync();
        var saved = await client.PostFormAsync("/Fuel/Create", await client.GetAsync("/Fuel/Create"), FillUp(vehicleId, ("Gamma", "")));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Contains("Enter the station", saved.Errors);
    }

    [DatabaseFact]
    public async Task A_split_fill_up_is_saved_and_listed_in_order()
    {
        var (client, vehicleId) = await SeedAsync();
        // "village" alone: Beta is the only brand with that station, so it's filled in.
        var saved = await client.PostFormAsync("/Fuel/Create", await client.GetAsync("/Fuel/Create"),
            FillUp(vehicleId, ("", "village"), ("Gamma", "Hamlet")));
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);

        var log = (await client.GetAsync($"/Fuel?vehicleId={vehicleId}")).Text;
        Assert.Contains("<td>Beta + Gamma</td>", log);
        Assert.Contains("<td>Village + Hamlet</td>", log);
    }

    [DatabaseFact]
    public async Task Saving_over_someone_elses_change_shows_a_warning()
    {
        var (client, _) = await SeedAsync();
        var edit = PageResponse.Href(Regex.Match((await client.GetAsync("/Fuel")).Html, "<a [^>]*title=\"Edit\"[^>]*>").Value)!;
        var tab1 = await client.GetAsync(edit);
        var tab2 = await client.GetAsync(edit);

        var first = await client.PostFormAsync(edit, tab1, new() { ["Entry.Litres"] = "41" });
        Assert.True(first.Status == HttpStatusCode.Redirect, first.Errors);
        var stale = await client.PostFormAsync(edit, tab2, new() { ["Entry.Litres"] = "42" });
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Contains("Someone changed this fill-up after you opened it", stale.Text);
    }
}
