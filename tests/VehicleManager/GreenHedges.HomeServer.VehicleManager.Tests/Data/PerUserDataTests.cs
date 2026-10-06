using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Data;

public class PerUserDataTests : DatabaseTest
{
    [DatabaseFact]
    public async Task New_rows_belong_to_the_signed_in_user()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db);
        Assert.Equal(UserId, vehicle.UserId);
        Assert.Equal(UserId, vehicle.Owner!.UserId);
    }

    [DatabaseFact]
    public async Task Users_only_see_their_own_data()
    {
        int vehicleId;
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db);
            vehicleId = vehicle.Id;
            await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
        }

        await using var other = TestDatabase.As(NewUser());
        Assert.False(await other.Vehicles.AnyAsync());
        Assert.False(await other.Owners.AnyAsync());
        Assert.False(await other.FuelEntries.AnyAsync());
        Assert.False(await other.FuelEntryStops.AnyAsync());
        Assert.False(await other.FuelBrands.AnyAsync());
        Assert.False(await other.FuelStations.AnyAsync());
        Assert.Null(await other.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId));
        Assert.Equal(0, await other.Vehicles.Where(v => v.Id == vehicleId).ExecuteDeleteAsync());
        Assert.Equal(0, await other.FuelEntries.Where(f => f.VehicleId == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(f => f.Notes, "changed")));
    }

    [DatabaseFact]
    public async Task Different_users_can_use_the_same_names()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db, "SAME 1");
        await using var other = TestDatabase.As(NewUser());
        await TestData.AddVehicleAsync(other, "SAME 1");   // same registration and owner name: no clash
        Assert.Equal(1, await other.Vehicles.CountAsync());
    }

    [DatabaseFact]
    public async Task Nothing_is_visible_or_saved_without_a_signed_in_user()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        await using var none = TestDatabase.As(null);
        Assert.False(await none.Vehicles.AnyAsync());
        none.Owners.Add(new Owner { Name = "Nobody's" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.SaveChangesAsync());
    }
}
