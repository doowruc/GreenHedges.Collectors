using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages.Places;

/// <param name="Brand">A station's brand; null for brands.</param>
/// <param name="Stations">A brand's number of stations; null for stations.</param>
public record FuelPlaceRow(int Id, string Name, string? Brand, int FillUps, int? Stations)
{
    /// <summary>Name to show in a list mixing several brands' stations, e.g. "Riverside (Asda)".</summary>
    public string FullName => Brand is null ? Name : $"{Name} ({Brand})";

    public bool InUse => FillUps > 0 || Stations > 0;
}

public enum SaveResult { Saved, NotFound, Conflict }

/// <summary>What the shared Brands/Stations pages need from either list.</summary>
public interface IFuelPlaceList
{
    /// <summary>The page title, e.g. "Brands".</summary>
    string Plural { get; }
    /// <summary>For messages, e.g. "brand".</summary>
    string Singular { get; }
    int MaxNameLength { get; }
    /// <summary>True for stations, which each belong to a brand.</summary>
    bool HasBrand { get; }

    Task<List<FuelPlaceRow>> ListAsync();
    Task<FuelPlaceRow?> FindAsync(int id);
    Task<FuelPlaceInput?> GetForEditAsync(int id);
    /// <summary>Tidies the input and adds any errors to <paramref name="state"/> (keys <c>Place.Name</c>, <c>Place.BrandId</c>).</summary>
    Task ValidateAsync(int id, FuelPlaceInput input, ModelStateDictionary state);
    /// <summary>
    /// Saves the edit, unless it changed since the form was loaded (a conflict). Returns its current
    /// RowVersion, to put back in the form after a conflict.
    /// </summary>
    Task<(SaveResult Result, byte[] RowVersion)> SaveAsync(int id, FuelPlaceInput input);
    /// <summary>Moves everything from one to the other, then deletes the first. Returns the fill-up stops moved.</summary>
    Task<int> MergeAsync(int fromId, int intoId);
    /// <summary>Deletes it if nothing uses it.</summary>
    Task<bool> DeleteIfUnusedAsync(int id);
}

/// <summary>Database work shared by the Brands and Stations pages.</summary>
public abstract class FuelPlaceList<T>(AppDbContext db, CurrentUser user) : IFuelPlaceList
    where T : AuditedEntity, IFuelPlace
{
    public abstract string Plural { get; }
    public abstract string Singular { get; }
    public abstract int MaxNameLength { get; }
    public abstract bool HasBrand { get; }

    protected AppDbContext Db => db;

    public abstract Task<List<FuelPlaceRow>> ListAsync();
    public abstract Task<FuelPlaceRow?> FindAsync(int id);
    public abstract Task<FuelPlaceInput?> GetForEditAsync(int id);
    /// <summary>Checks a tidied name and brand; add errors to <paramref name="state"/>.</summary>
    protected abstract Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state);
    protected abstract void Apply(FuelPlaceInput input, T place);
    protected abstract Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now);
    protected abstract Task<bool> InUseAsync(int id);

    public async Task ValidateAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        input.Name = FuelStops.NormaliseName(input.Name);
        if (input.Name.Length > MaxNameLength)
            state.AddModelError("Place.Name", $"The name can be at most {MaxNameLength} characters.");
        else if (input.Name.Length > 0)
            await CheckAsync(id, input, state);
    }

    public async Task<(SaveResult Result, byte[] RowVersion)> SaveAsync(int id, FuelPlaceInput input)
    {
        var place = await db.Set<T>().FirstOrDefaultAsync(p => p.Id == id);
        if (place is null) return (SaveResult.NotFound, []);

        Apply(input, place);
        if (await db.TrySaveEditAsync(place, input.RowVersion)) return (SaveResult.Saved, place.RowVersion);
        return db.Entry(place).State == EntityState.Detached
            ? (SaveResult.NotFound, [])
            : (SaveResult.Conflict, place.RowVersion);
    }

    public async Task<int> MergeAsync(int fromId, int intoId)
    {
        var userId = user.Id ?? throw new InvalidOperationException("Can't merge without a signed-in user.");
        var now = DateTime.UtcNow;
        // Bulk updates bypass SaveChanges, so the merge sets the audit columns itself. The retrying
        // execution strategy needs the whole transaction inside it.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var moved = await MergeCoreAsync(fromId, intoId, userId, now);
            await db.Set<T>().Where(p => p.Id == fromId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            return moved;
        });
    }

    public async Task<bool> DeleteIfUnusedAsync(int id) =>
        !await InUseAsync(id) && await db.Set<T>().Where(p => p.Id == id).ExecuteDeleteAsync() > 0;

    /// <summary>Points the stops at one station to another.</summary>
    protected Task<int> MoveStopsAsync(int fromStationId, int intoStationId, string userId, DateTime now) =>
        db.FuelEntryStops.Where(s => s.FuelStationId == fromStationId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.FuelStationId, intoStationId)
            .SetProperty(x => x.ModifiedBy, userId)
            .SetProperty(x => x.ModifiedDate, now));
}

public class FuelBrandList(AppDbContext db, CurrentUser user) : FuelPlaceList<FuelBrand>(db, user)
{
    public override string Plural => "Brands";
    public override string Singular => "brand";
    public override int MaxNameLength => FuelBrand.MaxNameLength;
    public override bool HasBrand => false;

    public override Task<List<FuelPlaceRow>> ListAsync() =>
        Rows(Db.FuelBrands.OrderBy(b => b.Name)).ToListAsync();

    public override Task<FuelPlaceRow?> FindAsync(int id) =>
        Rows(Db.FuelBrands.Where(b => b.Id == id)).FirstOrDefaultAsync();

    private static IQueryable<FuelPlaceRow> Rows(IQueryable<FuelBrand> brands) =>
        brands.Select(b => new FuelPlaceRow(b.Id, b.Name, null, b.Stations.SelectMany(s => s.Stops).Count(), b.Stations.Count));

    public override Task<FuelPlaceInput?> GetForEditAsync(int id) =>
        Db.FuelBrands.Where(b => b.Id == id)
            .Select(b => new FuelPlaceInput { Name = b.Name, RowVersion = b.RowVersion })
            .FirstOrDefaultAsync();

    protected override async Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        if (await Db.FuelBrands.AnyAsync(b => b.Id != id && b.Name == input.Name))
            state.AddModelError("Place.Name", "There's already a brand with this name. To combine them, use Merge instead.");
    }

    protected override void Apply(FuelPlaceInput input, FuelBrand brand) => brand.Name = input.Name;

    /// <summary>
    /// Moves the brand's stations to the other brand. A station whose name the other brand already
    /// has is combined with that one (its fill-ups move across); the rest just change brand.
    /// </summary>
    protected override async Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now)
    {
        var stations = await Db.FuelStations.Where(s => s.FuelBrandId == fromId)
            .Select(s => new { s.Id, s.Name, Stops = s.Stops.Count }).ToListAsync();
        var intoStations = await Db.FuelStations.Where(s => s.FuelBrandId == intoId)
            .ToDictionaryAsync(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var moved = 0;
        foreach (var station in stations)
        {
            if (intoStations.TryGetValue(station.Name, out var twin))
            {
                moved += await MoveStopsAsync(station.Id, twin, userId, now);
                await Db.FuelStations.Where(s => s.Id == station.Id).ExecuteDeleteAsync();
            }
            else
            {
                moved += station.Stops;
                await Db.FuelStations.Where(s => s.Id == station.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.FuelBrandId, intoId)
                    .SetProperty(x => x.ModifiedBy, userId)
                    .SetProperty(x => x.ModifiedDate, now));
            }
        }
        return moved;
    }

    protected override Task<bool> InUseAsync(int id) => Db.FuelStations.AnyAsync(s => s.FuelBrandId == id);
}

public class FuelStationList(AppDbContext db, CurrentUser user) : FuelPlaceList<FuelStation>(db, user)
{
    public override string Plural => "Stations";
    public override string Singular => "station";
    public override int MaxNameLength => FuelStation.MaxNameLength;
    public override bool HasBrand => true;

    public override Task<List<FuelPlaceRow>> ListAsync() =>
        Rows(Db.FuelStations.OrderBy(s => s.Name).ThenBy(s => s.FuelBrand!.Name)).ToListAsync();

    public override Task<FuelPlaceRow?> FindAsync(int id) =>
        Rows(Db.FuelStations.Where(s => s.Id == id)).FirstOrDefaultAsync();

    private static IQueryable<FuelPlaceRow> Rows(IQueryable<FuelStation> stations) =>
        stations.Select(s => new FuelPlaceRow(s.Id, s.Name, s.FuelBrand!.Name, s.Stops.Count, null));

    public override Task<FuelPlaceInput?> GetForEditAsync(int id) =>
        Db.FuelStations.Where(s => s.Id == id)
            .Select(s => new FuelPlaceInput { Name = s.Name, BrandId = s.FuelBrandId, RowVersion = s.RowVersion })
            .FirstOrDefaultAsync();

    protected override async Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        if (input.BrandId is not { } brandId || !await Db.FuelBrands.AnyAsync(b => b.Id == brandId))
            state.AddModelError("Place.BrandId", "Choose the brand.");
        else if (await Db.FuelStations.AnyAsync(s => s.Id != id && s.FuelBrandId == brandId && s.Name == input.Name))
            state.AddModelError("Place.Name", "That brand already has a station with this name. To combine them, use Merge instead.");
    }

    protected override void Apply(FuelPlaceInput input, FuelStation station)
    {
        station.Name = input.Name;
        station.FuelBrandId = input.BrandId!.Value;
    }

    protected override Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now) =>
        MoveStopsAsync(fromId, intoId, userId, now);

    protected override Task<bool> InUseAsync(int id) => Db.FuelEntryStops.AnyAsync(s => s.FuelStationId == id);
}
