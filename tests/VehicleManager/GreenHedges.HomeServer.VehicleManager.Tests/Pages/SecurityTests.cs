using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class SecurityTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseTheory]
    [InlineData("/")]
    [InlineData("/Vehicles")]
    [InlineData("/FuelBrands")]
    [InlineData("/FuelLog/Create")]
    [InlineData("/Backup?handler=Download")]
    public async Task Pages_need_a_signed_in_user(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientFor(null).GetAsync(url)).Status);
    }

    [DatabaseFact]
    public async Task Another_users_vehicle_is_not_found()
    {
        int vehicleId;
        await using (var db = Db()) vehicleId = (await TestData.AddVehicleAsync(db)).Id;

        var stranger = app.ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Vehicles/Details/{vehicleId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Vehicles/Edit/{vehicleId}")).Status);
    }

    [DatabaseFact]
    public async Task A_form_cant_set_the_id_of_what_it_saves()
    {
        int existingId;
        string ownerId;
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db, "KEEP 1");
            (existingId, ownerId) = (vehicle.Id, vehicle.OwnerId.ToString());
        }

        var client = app.ClientFor(UserId);
        var created = await client.PostFormAsync("/Vehicles/Create", await client.GetAsync("/Vehicles/Create"), new()
        {
            ["Vehicle.Id"] = existingId.ToString(), ["Vehicle.OwnerId"] = ownerId, ["Vehicle.Type"] = "Car",
            ["Vehicle.Make"] = "New", ["Vehicle.Model"] = "Car", ["Vehicle.RegistrationNumber"] = "NEW 1"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.Status);

        await using var check = Db();
        Assert.Equal("KEEP 1", (await check.Vehicles.SingleAsync(v => v.Id == existingId)).RegistrationNumber);
        Assert.Equal(2, await check.Vehicles.CountAsync());
    }
}
