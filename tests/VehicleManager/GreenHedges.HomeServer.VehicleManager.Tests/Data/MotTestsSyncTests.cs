using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Data;

public class MotTestsSyncTests : DatabaseTest
{
    private static readonly DateTimeOffset First = new(2024, 11, 3, 10, 30, 0, TimeSpan.Zero), Second = First.AddYears(1);

    private static MotHistoryTest Test(DateTimeOffset completed, string number, params MotHistoryDefect[] defects) =>
        new(completed, "PASSED", DateOnly.FromDateTime(completed.AddYears(1).UtcDateTime), "30000", "MI", "READ", number, "AB12 CDE", "DVSA", [.. defects]);

    private async Task<int> SyncAsync(params MotHistoryTest[] tests)
    {
        await using var db = Db();
        var vehicle = await db.Vehicles.SingleAsync();
        await MotTests.SyncAsync(db, vehicle, tests);
        return await db.SaveChangesAsync();
    }

    private async Task<List<string>> StoredAsync()
    {
        await using var db = Db();
        return await db.MotTests.OrderBy(t => t.CompletedDate)
            .Select(t => t.MotTestNumber + ": " + string.Join(", ", t.Defects.OrderBy(d => d.Sequence).Select(d => d.Sequence + " " + d.Text)))
            .ToListAsync();
    }

    [DatabaseFact]
    public async Task Stored_tests_follow_the_DVSA()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        var advisory = new MotHistoryDefect("Tyre worn", "ADVISORY", false);
        var corroded = new MotHistoryDefect("Brake pipe corroded", "MINOR", false);

        await SyncAsync(Test(First, "111"), Test(Second, "222", advisory));
        Assert.Equal(["111: ", "222: 1 Tyre worn"], await StoredAsync());

        // The same history again changes nothing, so the audit columns stay put.
        Assert.Equal(0, await SyncAsync(Test(First, "111"), Test(Second, "222", advisory)));

        // A test the DVSA no longer has goes; a changed one is updated in place, defects included.
        await SyncAsync(Test(Second, "222", corroded, advisory), Test(Second.AddYears(1), "333"));
        Assert.Equal(["222: 1 Brake pipe corroded, 2 Tyre worn", "333: "], await StoredAsync());

        await SyncAsync(Test(Second, "222"), Test(Second.AddYears(1), "333"));
        Assert.Equal(["222: ", "333: "], await StoredAsync());
        await using (var db = Db()) Assert.Equal(0, await db.MotDefects.CountAsync());
    }

    [DatabaseFact]
    public async Task Other_users_MOT_tests_arent_seen()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        await SyncAsync(Test(First, "111", new MotHistoryDefect("Tyre worn", "ADVISORY", false)));

        await using var other = TestDatabase.As(NewUser());
        Assert.Equal((0, 0), (await other.MotTests.CountAsync(), await other.MotDefects.CountAsync()));
    }
}
