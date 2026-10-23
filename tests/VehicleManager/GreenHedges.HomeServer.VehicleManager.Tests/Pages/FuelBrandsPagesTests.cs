using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

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
        Regex.Matches(page.Html, "<a [^>]*title=\"(?:Stations|Fill-ups and prices|Edit|Merge into another|Delete)\"[^>]*>").Select(m => m.Value).ToList();

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
        Assert.Contains("<h1 class=\"h3 mb-0\">Alpha stations</h1>", stations.Html);
        Assert.Contains("<td>Town</td>", stations.Html);
        Assert.DoesNotContain("<td>Village</td>", stations.Html);   // Beta's
        await AssertAllLinksLoadAsync(client, stations);

        var edit = await client.GetAsync(stations.RowLink("Town", "Edit")!);
        Assert.Matches(@"<a [^>]*href=""/FuelBrands/\d+/Stations""[^>]*>Cancel</a>", edit.Html);
    }

    [DatabaseFact]
    public async Task Station_pages_have_a_Back_button_or_Cancel_instead_of_breadcrumbs()
    {
        var client = await SeedAsync();
        var stationsUrl = (await client.GetAsync("/FuelBrands")).RowLink("Alpha", "Stations")!;
        var stations = await client.GetAsync(stationsUrl);
        var stationUrl = stations.RowLink("Town", "Fill-ups and prices")!;

        static string Back(string href) =>
            $@"<a href=""{Regex.Escape(href)}"" class=""btn btn-sm btn-outline-secondary""><i class=""bi bi-arrow-left me-1""></i>Back</a>";
        static string Header(string title) => $@"<h1 class=""h3 mb-0"">{Regex.Escape(title)}</h1>\s*";

        // The list goes back to the brands.
        Assert.Matches(Header("Alpha stations") + Back("/FuelBrands"), stations.Html);
        // Edit, Merge and Delete have Cancel in their forms instead.
        foreach (var (action, title) in new[] { ("Edit", "Edit station"), ("Merge", "Merge station"), ("Delete", "Delete station") })
        {
            var html = (await client.GetAsync(stations.RowLink("Town", action)!)).Html;
            Assert.Contains($"<h1 class=\"h3 mb-3\">{title}</h1>", html);
            Assert.DoesNotContain("bi-arrow-left", html);
            Assert.Matches($@"<a [^>]*href=""{Regex.Escape(stationsUrl)}""[^>]*>(?:Cancel|Back)</a>", html);   // Back in place of Cancel when a station in use can't be deleted
        }
        foreach (var action in new[] { "Edit", "Merge", "Delete" })
            Assert.DoesNotContain("breadcrumb", (await client.GetAsync(stations.RowLink("Town", action)!)).Html);
        Assert.DoesNotContain("breadcrumb", stations.Html);

        // The station page's Back, beside Edit and Merge, goes back to wherever it was opened from, else the list.
        var station = (await client.GetAsync(stationUrl)).Html;
        Assert.Matches(@">Merge</a>\s*(?:@\*.*?\*@\s*)?<a href=""" + Regex.Escape(stationsUrl) + @""" class=""btn btn-sm btn-outline-secondary"" data-history-back=""true""><i class=""bi bi-arrow-left me-1""></i>Back</a>", station);
        Assert.Contains("history.back()", station);
    }

    [DatabaseFact]
    public async Task A_stations_page_lists_its_fill_ups_and_prices_by_fuel_type()
    {
        var client = await SeedAsync();   // the car's Alpha Town fill-up is on 1 Jan
        await using (var db = Db())
        {
            var car = db.Vehicles.Single();
            var van = await TestData.AddVehicleAsync(db, "VAN 1");
            van.FuelType = GreenHedges.HomeServer.VehicleManager.Models.FuelType.Diesel;
            await db.SaveChangesAsync();
            await TestData.AddFillUpAsync(db, van.Id, new DateOnly(2026, 1, 5), ("Alpha", "Town"));
            var split = await TestData.AddFillUpAsync(db, car.Id, new DateOnly(2026, 1, 6), ("Beta", "Village"), ("Alpha", "Town"));
            split.CostPerLitre = 1.459m;
            await db.SaveChangesAsync();
        }
        var stations = await client.GetAsync((await client.GetAsync("/FuelBrands")).RowLink("Alpha", "Stations")!);
        var page = await client.GetAsync(stations.RowLink("Town", "Fill-ups and prices")!);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("<h1 class=\"h3 mb-0\">Alpha Town</h1>", page.Html);
        Assert.DoesNotContain("breadcrumb", page.Html);

        // A tab per fuel type; the latest fill-up's (the car's split one, unleaded) is shown first.
        var html = Regex.Replace(page.Html, @"\s+", " ");
        Assert.Matches(@"class=""nav-link active""[^>]*data-bs-target=""#fuel-Unleaded""", html);
        Assert.Matches(@"class=""nav-link ?""[^>]*data-bs-target=""#fuel-Diesel""", html);
        // Small pills and buttons, like the rest of the app's.
        Assert.Contains("<ul class=\"nav nav-pills nav-pills-sm mb-2\" role=\"tablist\" aria-label=\"Fuel type\">", html);
        Assert.Matches(@"<a class=""btn btn-sm btn-outline-secondary"" href=""/FuelBrands/\d+/Stations/Edit/\d+"">Edit</a>", html);
        Assert.Matches(@"<a class=""btn btn-sm btn-outline-secondary"" href=""/FuelBrands/\d+/Stations/Merge/\d+"">Merge</a>", html);
        var unleadedStart = html.IndexOf("id=\"fuel-Unleaded\"");
        var dieselStart = html.IndexOf("id=\"fuel-Diesel\"");
        Assert.True(unleadedStart >= 0 && dieselStart > unleadedStart);
        // Each section's visible rows (not the footer's sizing rows, which are shared between them).
        static string Rows(string section) => Regex.Match(section, "<tbody>.*?</tbody>", RegexOptions.Singleline).Value;
        var unleaded = Rows(html[unleadedStart..dieselStart]);
        var diesel = Rows(html[dieselStart..]);
        Assert.Contains("class=\"tab-pane show active\"", html[(unleadedStart - 60)..unleadedStart]);

        // Each table has that fuel type's fill-ups here (split ones too), newest first; none from elsewhere.
        Assert.Matches("06/01/2026.*01/01/2026", unleaded);
        Assert.DoesNotContain("05/01/2026", unleaded);
        Assert.Contains("05/01/2026", diesel);
        Assert.DoesNotContain("01/01/2026", diesel);
        Assert.DoesNotContain("03/01/2026", page.Html);   // Beta Village only
        Assert.Contains("/js/data-table.", page.Html);

        // The charts plot the pump price, oldest first.
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains("""data-price-chart="[{"date":"2026-01-01","price":1.500},{"date":"2026-01-06","price":1.459}]""", text);
        Assert.Contains("""data-price-chart="[{"date":"2026-01-05","price":1.500}]""", text);
    }

    [DatabaseFact]
    public async Task A_fuel_log_link_opens_a_station_at_that_fill_ups_fuel_type()
    {
        var client = await SeedAsync();   // the car (unleaded) filled up at Alpha Town on 1 Jan
        await using (var db = Db())
        {
            var van = await TestData.AddVehicleAsync(db, "VAN 1");
            van.FuelType = GreenHedges.HomeServer.VehicleManager.Models.FuelType.Diesel;
            await db.SaveChangesAsync();
            await TestData.AddFillUpAsync(db, van.Id, new DateOnly(2025, 12, 1), ("Alpha", "Town"));   // earlier
        }
        static string Active(PageResponse page) =>
            Regex.Match(page.Html, @"class=""nav-link active""[^>]*data-bs-target=""#fuel-(\w+)""").Groups[1].Value;

        var log = (await client.GetAsync("/FuelLog")).Html;
        var dieselLink = PageResponse.Href(Regex.Match(log, @"<a href=""[^""]*\?fuelType=Diesel"">Alpha Town</a>").Value)!;
        var unleadedLink = PageResponse.Href(Regex.Match(log, @"<a href=""[^""]*\?fuelType=Unleaded"">Alpha Town</a>").Value)!;
        Assert.Equal("Diesel", Active(await client.GetAsync(dieselLink)));
        Assert.Equal("Unleaded", Active(await client.GetAsync(unleadedLink)));

        // Without one, or with one the station hasn't sold, it opens at the latest fill-up's (unleaded).
        var station = dieselLink.Split('?')[0];
        Assert.Equal("Unleaded", Active(await client.GetAsync(station)));
        Assert.Equal("Unleaded", Active(await client.GetAsync(station + "?fuelType=Petrol")));

        // Each tab links to its own URL, which the page puts in the address bar on switching, so a refresh
        // (or the link) stays on that fuel type rather than the one the page was opened with.
        var opened = await client.GetAsync(unleadedLink);
        var dieselTab = PageResponse.Href(Regex.Match(opened.Html, @"<a [^>]*data-bs-target=""#fuel-Diesel""[^>]*>").Value);
        Assert.Equal(dieselLink, dieselTab);
        Assert.Equal("Diesel", Active(await client.GetAsync(dieselTab!)));
        Assert.Contains("history.replaceState", opened.Html);
    }

    [DatabaseFact]
    public async Task A_stations_fuel_type_tables_are_sized_alike_so_columns_dont_move_between_tabs()
    {
        var client = await SeedAsync();   // "Test Car" (unleaded) at Alpha Town
        await using (var db = Db())
        {
            var van = await TestData.AddVehicleAsync(db, "VAN 1");
            (van.Make, van.Model, van.FuelType) = ("Volkswagen", "Transporter Kombi", GreenHedges.HomeServer.VehicleManager.Models.FuelType.Diesel);
            await db.SaveChangesAsync();
            await TestData.AddFillUpAsync(db, van.Id, new DateOnly(2026, 1, 5), ("Alpha", "Town"));
        }
        var stations = await client.GetAsync((await client.GetAsync("/FuelBrands")).RowLink("Alpha", "Stations")!);
        var html = (await client.GetAsync(stations.RowLink("Town", "Fill-ups and prices")!)).Html;

        // Each table's hidden footer holds the widest rows from both fuel types, so its columns are as wide
        // as the other's: the unleaded table is sized for the diesel van's longer name.
        var footers = Regex.Matches(html, @"<tfoot class=""column-sizer"" aria-hidden=""true"">(.*?)</tfoot>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, footers.Count);
        Assert.Equal(footers[0], footers[1]);
        Assert.Contains(">Volkswagen Transporter Kombi</a>", footers[0]);
        Assert.Contains(">Test Car</a>", footers[0]);   // every vehicle name, as letters' widths vary
        var unleadedBody = Regex.Match(html[html.IndexOf("id=\"fuel-Unleaded\"")..], "<tbody>.*?</tbody>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("Volkswagen", unleadedBody);

        // DataTables mustn't fix the widths from each table's own rows, and the figures are sized by their number of
        // characters, so every digit has to be the same width.
        Assert.Contains("autoWidth: false", (await client.GetAsync("/js/data-table.js")).Html);
        var css = (await client.GetAsync(Regex.Match(html, @"href=""(/css/site[^""]*\.css)""").Groups[1].Value)).Html;
        Assert.Matches(@"\.table-data td, \.table-data th \{[^}]*font-variant-numeric: tabular-nums", css);
    }

    [DatabaseFact]
    public async Task A_station_with_one_fuel_type_has_no_fuel_type_tabs()
    {
        var client = await SeedAsync();
        var stations = await client.GetAsync((await client.GetAsync("/FuelBrands")).RowLink("Beta", "Stations")!);
        var page = await client.GetAsync(stations.RowLink("Village", "Fill-ups and prices")!);
        Assert.DoesNotContain("role=\"tablist\"", page.Html);
        Assert.Contains("id=\"fuel-Unleaded\"", page.Html);
        Assert.Contains("03/01/2026", page.Html);
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
