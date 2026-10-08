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
    public async Task Add_fill_up_form_starts_blank_and_suggests_stations_by_brand()
    {
        var (client, _) = await SeedAsync();
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db, "ZZ99 ZZZ");
            await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2025, 6, 1), ("Beta", "Abbey"), ("Gamma", "Hamlet"));
        }
        var form = await client.GetAsync("/FuelLog/Create");
        Assert.Equal(HttpStatusCode.OK, form.Status);
        // Not pre-filled from the last fill-up (Beta Village).
        Assert.Matches("<input [^>]*name=\"Entry.Stops\\[0\\].Brand\"[^>]*value=\"\"", form.Html);
        Assert.Matches("<input [^>]*name=\"Entry.Stops\\[0\\].Station\"[^>]*value=\"\"", form.Html);
        // Every brand is suggested; stations are listed per brand (lower case) for the script to filter by.
        // The suggestions are a dropdown (typeahead.js), not the browser's own <datalist>.
        Assert.Contains("/js/typeahead.", form.Html);
        Assert.DoesNotContain("<datalist", form.Html);
        Assert.Contains("data-brands=\"[\"Beta\",\"Gamma\"]\"", form.Text);
        Assert.Contains("data-stations-by-brand=\"{\"beta\":[\"Abbey\",\"Village\"],\"gamma\":[\"Hamlet\"]}\"", form.Text);
    }

    [DatabaseFact]
    public async Task Station_is_disabled_until_a_brand_is_entered()
    {
        var (client, _) = await SeedAsync();
        var form = await client.GetAsync("/FuelLog/Create");
        Assert.Matches("<input [^>]*name=\"Entry.Stops\\[0\\].Brand\"", form.Html);
        Assert.DoesNotMatch("<input [^>]*disabled[^>]*name=\"Entry.Stops\\[0\\].Brand\"|name=\"Entry.Stops\\[0\\].Brand\"[^>]*disabled", form.Html);
        Assert.Matches("<input [^>]*name=\"Entry.Stops\\[0\\].Station\"[^>]*disabled=\"disabled\"|<input [^>]*disabled=\"disabled\"[^>]*name=\"Entry.Stops\\[0\\].Station\"", form.Html);
    }

    [DatabaseFact]
    public async Task A_brand_without_a_station_is_rejected()
    {
        var (client, vehicleId) = await SeedAsync();
        var saved = await client.PostFormAsync("/FuelLog/Create", await client.GetAsync("/FuelLog/Create"), FillUp(vehicleId, ("Gamma", "")));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Contains("Enter the station", saved.Errors);
    }

    [DatabaseFact]
    public async Task A_split_fill_up_is_saved_and_listed_in_order()
    {
        var (client, vehicleId) = await SeedAsync();
        // "village" alone: Beta is the only brand with that station, so it's filled in.
        var saved = await client.PostFormAsync("/FuelLog/Create", await client.GetAsync("/FuelLog/Create"),
            FillUp(vehicleId, ("", "village"), ("Gamma", "Hamlet")));
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);

        // Brand and station share one "Fuel Station" column, on the combined log and the vehicle's page,
        // each stop linking to its station's page, opened at the vehicle's fuel type.
        const string link = @"<a href=""/FuelBrands/\d+/Stations/\d+\?fuelType=Unleaded"">";
        foreach (var url in new[] { "/FuelLog", $"/FuelLog?vehicleId={vehicleId}", $"/Vehicles/Details/{vehicleId}" })
        {
            var log = Regex.Replace((await client.GetAsync(url)).Text, @"\s+", " ");
            Assert.Contains("<th>Fuel Station</th>", log);
            Assert.DoesNotContain("<th>Brand</th>", log);
            Assert.Matches($"<td> {link}Beta Village</a> \\+ {link}Gamma Hamlet</a> </td>", log);
            Assert.Matches($"<td> {link}Beta Village</a> </td>", log);   // the seeded single-station fill-up
        }

        var hamlet = PageResponse.Href(Regex.Match((await client.GetAsync("/FuelLog")).Html, "<a [^>]*>Gamma Hamlet</a>").Value)!;
        var station = await client.GetAsync(hamlet);
        Assert.Equal(HttpStatusCode.OK, station.Status);
        Assert.Contains("<h1 class=\"h3 mb-0\">Gamma Hamlet</h1>", station.Html);
    }

    [DatabaseFact]
    public async Task Saving_over_someone_elses_change_shows_a_warning()
    {
        var (client, _) = await SeedAsync();
        var edit = PageResponse.Href(Regex.Match((await client.GetAsync("/FuelLog")).Html, "<a [^>]*title=\"Edit\"[^>]*>").Value)!;
        var tab1 = await client.GetAsync(edit);
        var tab2 = await client.GetAsync(edit);

        var first = await client.PostFormAsync(edit, tab1, new() { ["Entry.Litres"] = "41" });
        Assert.True(first.Status == HttpStatusCode.Redirect, first.Errors);
        var stale = await client.PostFormAsync(edit, tab2, new() { ["Entry.Litres"] = "42" });
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Contains("Someone changed this fill-up after you opened it", stale.Text);
    }

    [DatabaseFact]
    public async Task Fuel_log_and_fill_up_pages_are_under_FuelLog()
    {
        var (client, _) = await SeedAsync();
        var log = await client.GetAsync("/FuelLog");
        Assert.Equal(HttpStatusCode.OK, log.Status);
        Assert.Contains("<title>Fuel Log ", log.Html);
        Assert.Contains("<h1 class=\"h3 mb-0\">Fuel Log</h1>", log.Html);
        Assert.Matches(@"<a [^>]*href=""/FuelLog/Edit/\d+""", log.Html);
        Assert.Matches(@"<a [^>]*href=""/FuelLog/Delete/\d+""", log.Html);
        Assert.Matches(@"<a class=""btn btn-success btn-sm"" href=""/FuelLog/Create"">", log.Html);   // Add fill-up, in the menu bar
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Fuel")).Status);          // the old addresses are gone
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Fuel/Create")).Status);
    }

    [DatabaseFact]
    public async Task Fuel_log_and_vehicle_page_tables_use_DataTables()
    {
        var (client, vehicleId) = await SeedAsync();
        foreach (var url in new[] { "/FuelLog", $"/Vehicles/Details/{vehicleId}" })
        {
            var page = (await client.GetAsync(url)).Html;
            Assert.Contains("datatables.net@", page);
            Assert.Contains("dataTables.bootstrap5.min.css", page);
            Assert.Matches(@"<script src=""/js/data-table[^""]*\.js""", page);
            Assert.Matches(@"<div class=""card-body py-2"">\s*<div class=""data-table dt-pending"" data-noun=""fill-ups"">", page);   // same padding as the stat cards; first page only until it loads
            Assert.Matches(@"<td data-order=""2026-01-01"">01/01/2026</td>", page);   // dates sort as dates
            Assert.Contains("<th data-orderable=\"false\" data-searchable=\"false\"></th>", page);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/data-table.js")).Status);
    }

    [DatabaseFact]
    public async Task The_vehicle_pages_Add_fill_up_is_on_the_Fuel_tab_beside_the_period_toggle()
    {
        var (client, vehicleId) = await SeedAsync();
        var page = (await client.GetAsync($"/Vehicles/Details/{vehicleId}")).Html;
        var main = page[page.IndexOf("<main", StringComparison.Ordinal)..];   // not the menu bar's own Add fill-up
        var tabs = main.IndexOf("<ul class=\"nav nav-tabs", StringComparison.Ordinal);

        Assert.DoesNotContain("/FuelLog/Create", main[..tabs]);   // gone from the header
        var row = Regex.Match(main, @"<div class=""period-toggle-row d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2"">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("aria-label=\"Stats period\"", row);
        Assert.Matches($@"<a class=""btn btn-success"" aria-label=""Add fill-up"" href=""/FuelLog/Create\?vehicleId={vehicleId}""><i class=""bi bi-fuel-pump me-1""></i>Add<span class=""d-none d-sm-inline""> fill-up</span></a>\s*$", row);   // last, so right-aligned
        Assert.True(main.IndexOf("period-toggle-row", StringComparison.Ordinal) > main.IndexOf("id=\"tab-fuel\"", StringComparison.Ordinal));
    }

    [DatabaseFact]
    public async Task Vehicle_chart_fills_a_sized_box_rather_than_a_fixed_ratio()
    {
        var (client, vehicleId) = await SeedAsync();
        var page = (await client.GetAsync($"/Vehicles/Details/{vehicleId}")).Html;
        Assert.Contains("<div class=\"chart-box\"><canvas id=\"chart\"></canvas></div>", page);   // no fixed height
        Assert.Contains("maintainAspectRatio: false", page);
    }

    [DatabaseFact]
    public async Task Fuel_log_lists_newest_first_then_by_vehicle()
    {
        var (client, firstVehicleId) = await SeedAsync();   // "Test Car", fill-up on 01/01/2026
        await using (var db = Db())
        {
            // Added second but sorts first by name, on the same date.
            var alpha = new VehicleManager.Models.Vehicle { OwnerId = db.Vehicles.Single().OwnerId, Make = "Alpha", Model = "Van", RegistrationNumber = "ALPHA 1" };
            db.Vehicles.Add(alpha);
            await db.SaveChangesAsync();
            await TestData.AddFillUpAsync(db, alpha.Id, new DateOnly(2026, 1, 1), ("Beta", "Village"));
            await TestData.AddFillUpAsync(db, firstVehicleId, new DateOnly(2026, 1, 2), ("Beta", "Village"));
        }
        var log = (await client.GetAsync("/FuelLog")).Html;
        var body = Regex.Match(log, "<tbody>.*</tbody>", RegexOptions.Singleline).Value;   // not the footer's sizing rows
        var vehicles = Regex.Matches(body, @"<td data-order=""([\d-]+)"">[^<]*</td>\s*<td><a [^>]*>([^<]+)</a></td>")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").ToList();
        Assert.Equal(["2026-01-02 Test Car", "2026-01-01 Alpha Van", "2026-01-01 Test Car"], vehicles);
        Assert.Contains("<th data-column=\"vehicle\">Vehicle</th>", log);   // how data-table.js finds the column to sort by
        Assert.DoesNotContain("data-column=\"vehicle\"", (await client.GetAsync($"/Vehicles/Details/{firstVehicleId}")).Html);
    }

    [DatabaseFact]
    public async Task Page_size_and_the_pending_row_limit_agree()
    {
        var (client, _) = await SeedAsync();
        var script = (await client.GetAsync("/js/data-table.js")).Html;
        var css = (await client.GetAsync((Regex.Match((await client.GetAsync("/FuelLog")).Html, @"href=""(/css/site[^""]*\.css)""").Groups[1].Value))).Html;
        var pageLength = int.Parse(Regex.Match(script, @"pageLength: (\d+)").Groups[1].Value);
        Assert.Equal(10, pageLength);
        Assert.Contains($".dt-pending .table-data tbody tr:nth-child(n+{pageLength + 1})", css);
    }

    [DatabaseFact]
    public async Task Real_mpg_always_shows_the_difference_from_the_dash()
    {
        var (client, vehicleId) = await SeedAsync();
        await using (var db = Db())
        {
            // 40 litres = 8.799 gallons. 400 miles -> 45.5 real MPG.
            foreach (var (day, dash) in new[] { (2, 48.0m), (3, 45.8m), (4, 45.5m), (5, 45.2m) })
            {
                var e = await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 2, day), ("Beta", "Village"));
                (e.TripMiles, e.TripAverageMpg) = (400, dash);
            }
            await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 2, 6), ("Beta", "Village"));   // no trip miles: no Real MPG
            await db.SaveChangesAsync();
        }
        var log = (await client.GetAsync($"/FuelLog?vehicleId={vehicleId}")).Text;
        string RealMpg(string date) => Regex.Replace(
            // The Real MPG cell is the row's second sort-value cell, after £/Litre.
            Regex.Match(log, $@"<td data-order=""{date}"">(?:.*?<td class=""text-end"" data-order=""[^""]*"">){{2}}(.*?)</td>", RegexOptions.Singleline).Groups[1].Value, @"\s+", " ").Trim();

        Assert.Equal(@"45.5 <small class=""text-optimistic"" title=""Dash minus real"">(+2.5)</small>", RealMpg("2026-02-02"));
        Assert.Equal(@"45.5 <small class=""text-optimistic"" title=""Dash minus real"">(+0.3)</small>", RealMpg("2026-02-03"));    // under 0.5: shown too
        Assert.Equal(@"45.5 <small class=""text-body-secondary"" title=""Dash minus real"">(0.0)</small>", RealMpg("2026-02-04")); // exact match
        Assert.Equal(@"45.5 <small class=""text-pessimistic"" title=""Dash minus real"">(-0.3)</small>", RealMpg("2026-02-05"));
        Assert.Equal("", RealMpg("2026-02-06"));
    }
}
