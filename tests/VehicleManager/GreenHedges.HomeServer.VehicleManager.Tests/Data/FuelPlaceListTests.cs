using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Pages.Places;
using VehicleManager.Tests.Infrastructure;
using VehicleManager.ViewModels;

namespace VehicleManager.Tests.Data;

public class FuelPlaceListTests : DatabaseTest
{
    /// <summary>Alpha@Town, Alpa@Town (a misspelt brand), Alpa@Village, Beta@Town, and an unused brand.</summary>
    private async Task SeedAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db);
        var day = 1;
        foreach (var stop in new[] { ("Alpha", "Town"), ("Alpa", "Town"), ("Alpa", "Village"), ("Beta", "Town") })
            await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, day++), stop);
        db.FuelBrands.Add(new() { Name = "Empty" });
        await db.SaveChangesAsync();
    }

    private FuelBrandList Brands(VehicleManager.Data.AppDbContext db) => new(db, new TestCurrentUser(UserId));
    private FuelStationList Stations(VehicleManager.Data.AppDbContext db) => new(db, new TestCurrentUser(UserId));

    [DatabaseFact]
    public async Task Lists_count_stations_and_fill_ups()
    {
        await SeedAsync();
        await using var db = Db();
        var brands = (await Brands(db).ListAsync()).ToDictionary(r => r.Name);
        Assert.Equal((2, 2), (brands["Alpa"].Stations!.Value, brands["Alpa"].FillUps));
        Assert.Equal(0, brands["Empty"].Stations);
        var stations = await Stations(db).ListAsync();
        Assert.Equal(["Town (Alpa)", "Town (Alpha)", "Town (Beta)", "Village (Alpa)"], stations.Select(s => s.FullName));
    }

    [DatabaseFact]
    public async Task Merging_a_brand_moves_its_stations_and_combines_same_named_ones()
    {
        await SeedAsync();
        await using (var db = Db())
        {
            var brands = (await Brands(db).ListAsync()).ToDictionary(r => r.Name);
            Assert.Equal(2, await Brands(db).MergeAsync(brands["Alpa"].Id, brands["Alpha"].Id));
        }
        await using (var db = Db())
        {
            var stations = (await Stations(db).ListAsync()).ToDictionary(r => r.FullName);
            Assert.Equal(["Town (Alpha)", "Town (Beta)", "Village (Alpha)"], stations.Keys);
            Assert.Equal(2, stations["Town (Alpha)"].FillUps);
            Assert.False(await db.FuelBrands.AnyAsync(b => b.Name == "Alpa"));
            Assert.True(await db.FuelEntryStops.Where(s => s.FuelStation!.Name == "Village").AllAsync(s => s.ModifiedBy == UserId));
        }
    }

    [DatabaseFact]
    public async Task Station_names_are_unique_per_brand_and_a_station_needs_a_brand()
    {
        await SeedAsync();
        await using var db = Db();
        var brands = (await Brands(db).ListAsync()).ToDictionary(r => r.Name);
        var village = (await Stations(db).ListAsync()).Single(s => s.Name == "Village").Id;

        var clash = new ModelStateDictionary();
        await Stations(db).ValidateAsync(village, new FuelPlaceInput { Name = "town", BrandId = brands["Alpa"].Id }, clash);
        Assert.Single(clash["Place.Name"]!.Errors);

        var noBrand = new ModelStateDictionary();
        await Stations(db).ValidateAsync(village, new FuelPlaceInput { Name = "Village" }, noBrand);
        Assert.Single(noBrand["Place.BrandId"]!.Errors);

        var otherBrand = new ModelStateDictionary();
        await Stations(db).ValidateAsync(village, new FuelPlaceInput { Name = "Town", BrandId = brands["Empty"].Id }, otherBrand);
        Assert.True(otherBrand.IsValid);
    }

    [DatabaseFact]
    public async Task A_station_can_change_brand_and_its_fill_ups_follow()
    {
        await SeedAsync();
        await using (var db = Db())
        {
            var beta = (await Brands(db).ListAsync()).Single(b => b.Name == "Beta").Id;
            var village = (await Stations(db).ListAsync()).Single(s => s.Name == "Village").Id;
            var form = (await Stations(db).GetForEditAsync(village))!;
            form.BrandId = beta;
            Assert.Equal(SaveResult.Saved, (await Stations(db).SaveAsync(village, form)).Result);
        }
        await using (var db = Db())
        {
            var entry = await db.FuelEntries.Where(f => f.Date == new DateOnly(2026, 1, 3)).Select(FuelEntryView.Projection).SingleAsync();
            Assert.Equal("Beta", entry.Brand);
        }
    }

    [DatabaseFact]
    public async Task Only_unused_brands_and_stations_can_be_deleted()
    {
        await SeedAsync();
        await using var db = Db();
        var brands = (await Brands(db).ListAsync()).ToDictionary(r => r.Name);
        var town = (await Stations(db).ListAsync()).First(s => s.Name == "Town").Id;
        Assert.False(await Brands(db).DeleteIfUnusedAsync(brands["Alpa"].Id));   // has stations
        Assert.False(await Stations(db).DeleteIfUnusedAsync(town));              // has fill-ups
        Assert.True(await Brands(db).DeleteIfUnusedAsync(brands["Empty"].Id));
    }
}
