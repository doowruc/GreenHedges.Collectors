using System.Net;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class VehiclePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Fuel_type_is_chosen_on_the_form_and_shown_on_the_vehicle_pages()
    {
        int ownerId;
        await using (var db = Db())
        {
            db.Owners.Add(new Owner { Name = "Me" });
            await db.SaveChangesAsync();
            ownerId = (await db.Owners.SingleAsync()).Id;
        }
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync("/Vehicles/Create");
        Assert.Contains("<option selected=\"selected\" value=\"0\">Unleaded</option>", form.Html);
        Assert.Contains("<option value=\"1\">Diesel</option>", form.Html);

        var created = await client.PostFormAsync("/Vehicles/Create", form, new()
        {
            ["Vehicle.OwnerId"] = ownerId.ToString(), ["Vehicle.FuelType"] = form.OptionValue("Diesel"),
            ["Vehicle.Make"] = "Ford", ["Vehicle.Model"] = "Transit", ["Vehicle.RegistrationNumber"] = "DIE 5EL"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.Status);

        int id;
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal(FuelType.Diesel, vehicle.FuelType);
            id = vehicle.Id;
        }
        Assert.Contains("<td>Diesel</td>", (await client.GetAsync("/Vehicles")).Html);
        Assert.Contains("Car · Diesel · Owned by Me", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);

        var edit = await client.GetAsync($"/Vehicles/Edit/{id}");
        Assert.Contains("<option selected=\"selected\" value=\"1\">Diesel</option>", edit.Html);
        var saved = await client.PostFormAsync($"/Vehicles/Edit/{id}", edit, new() { ["Vehicle.FuelType"] = edit.OptionValue("Unleaded") });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        await using var check = Db();
        Assert.Equal(FuelType.Unleaded, (await check.Vehicles.SingleAsync()).FuelType);
    }

    [DatabaseFact]
    public async Task Saving_a_vehicle_over_someone_elses_change_shows_a_warning()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db)).Id;
        var client = app.ClientFor(UserId);
        var url = $"/Vehicles/Edit/{id}";
        var tab1 = await client.GetAsync(url);
        var tab2 = await client.GetAsync(url);

        var first = await client.PostFormAsync(url, tab1, new() { ["Vehicle.Model"] = "First" });
        Assert.True(first.Status == HttpStatusCode.Redirect, first.Errors);
        var stale = await client.PostFormAsync(url, tab2, new() { ["Vehicle.Model"] = "Second" });
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Contains("Someone changed this vehicle after you opened it", stale.Text);
    }

    [DatabaseFact]
    public async Task Saving_an_owner_over_someone_elses_change_shows_a_warning()
    {
        int id;
        await using (var db = Db())
        {
            db.Owners.Add(new Owner { Name = "Me" });
            await db.SaveChangesAsync();
            id = (await db.Owners.SingleAsync()).Id;
        }
        var client = app.ClientFor(UserId);
        var url = $"/Owners/Edit/{id}";
        var tab1 = await client.GetAsync(url);
        var tab2 = await client.GetAsync(url);

        var first = await client.PostFormAsync(url, tab1, new() { ["Owner.Name"] = "First" });
        Assert.True(first.Status == HttpStatusCode.Redirect, first.Errors);
        var stale = await client.PostFormAsync(url, tab2, new() { ["Owner.Name"] = "Second" });
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Contains("after you opened it", stale.Text);
    }

    [DatabaseFact]
    public async Task The_vehicle_pages_header_controls_are_small_like_the_Fuel_Logs()
    {
        int id;
        await using (var db = Db())
        {
            id = (await TestData.AddVehicleAsync(db, "SMALL 1")).Id;
            await TestData.AddVehicleAsync(db, "SMALL 2");   // so the vehicle switcher shows
        }
        var page = (await app.ClientFor(UserId).GetAsync($"/Vehicles/Details/{id}")).Html;
        Assert.Contains("<select id=\"vehicle-switcher\" class=\"form-select form-select-sm w-auto\"", page);
        Assert.Matches($@"<a class=""btn btn-sm btn-outline-secondary"" href=""/Vehicles/Edit/{id}"">Edit</a>", page);
        Assert.Matches($@"<a class=""btn btn-sm btn-outline-danger"" href=""/Vehicles/Delete/{id}"">Delete</a>", page);
    }
}
