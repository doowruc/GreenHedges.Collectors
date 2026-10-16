using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Data;

public class MileageTrendDataTests : DatabaseTest
{
    [DatabaseFact]
    public async Task A_vehicle_with_only_MOT_readings_has_a_trend()
    {
        int id;
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db);
            vehicle.MotTests.Add(new MotTest { CompletedDate = new DateTime(2025, 6, 1, 10, 0, 0), Passed = true, Odometer = 16093, OdometerInKilometres = true });
            vehicle.MotTests.Add(new MotTest { CompletedDate = new DateTime(2026, 6, 1, 10, 0, 0), Passed = true, Odometer = 32187, OdometerInKilometres = true });
            vehicle.MotTests.Add(new MotTest { CompletedDate = new DateTime(2026, 7, 1, 10, 0, 0), Passed = true });   // odometer not read
            await db.SaveChangesAsync();
            id = vehicle.Id;
        }

        // 10,000 to 20,000 miles in a year.
        await using var check = Db();
        var trend = (await MileageTrend.ForVehiclesAsync(check))[id];
        Assert.Equal((new DateOnly(2026, 6, 1), 20000), (trend.LatestDate, trend.LatestOdometer));
        Assert.Equal(10000.0 / 365, trend.MilesPerDay, 6);
    }
}
