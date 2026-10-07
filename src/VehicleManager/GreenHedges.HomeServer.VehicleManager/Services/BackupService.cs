using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;

namespace VehicleManager.Services;

/// <summary>Backs up and restores everything the signed-in user owns (see <see cref="BackupFile"/>).</summary>
public class BackupService(AppDbContext db)
{
    public async Task<BackupCounts> CountAsync() => new(
        await db.Owners.CountAsync(), await db.Vehicles.CountAsync(), await db.FuelBrands.CountAsync(),
        await db.FuelStations.CountAsync(), await db.FuelEntries.CountAsync());

    public async Task<BackupFile> CreateAsync() => new()
    {
        Format = BackupFile.FormatName,
        Version = BackupFile.CurrentVersion,
        CreatedDate = DateTime.UtcNow,
        Owners = await db.Owners.OrderBy(o => o.Id).Select(o => new OwnerBackup
        {
            Id = o.Id, Name = o.Name,
            AddedDate = o.AddedDate, AddedBy = o.AddedBy, ModifiedDate = o.ModifiedDate, ModifiedBy = o.ModifiedBy
        }).ToListAsync(),
        Vehicles = await db.Vehicles.OrderBy(v => v.Id).Select(v => new VehicleBackup
        {
            Id = v.Id, OwnerId = v.OwnerId, Type = v.Type, FuelType = v.FuelType, Make = v.Make, Model = v.Model,
            RegistrationNumber = v.RegistrationNumber, RegistrationDate = v.RegistrationDate, PurchasedDate = v.PurchasedDate,
            AddedDate = v.AddedDate, AddedBy = v.AddedBy, ModifiedDate = v.ModifiedDate, ModifiedBy = v.ModifiedBy
        }).ToListAsync(),
        FuelBrands = await db.FuelBrands.OrderBy(b => b.Id).Select(b => new FuelBrandBackup
        {
            Id = b.Id, Name = b.Name,
            AddedDate = b.AddedDate, AddedBy = b.AddedBy, ModifiedDate = b.ModifiedDate, ModifiedBy = b.ModifiedBy
        }).ToListAsync(),
        FuelStations = await db.FuelStations.OrderBy(s => s.Id).Select(s => new FuelStationBackup
        {
            Id = s.Id, FuelBrandId = s.FuelBrandId, Name = s.Name,
            AddedDate = s.AddedDate, AddedBy = s.AddedBy, ModifiedDate = s.ModifiedDate, ModifiedBy = s.ModifiedBy
        }).ToListAsync(),
        FuelEntries = await db.FuelEntries.OrderBy(f => f.Id).Select(f => new FuelEntryBackup
        {
            Id = f.Id, VehicleId = f.VehicleId, Date = f.Date, Litres = f.Litres, CostPerLitre = f.CostPerLitre,
            VoucherPerLitre = f.VoucherPerLitre, TripAverageMpg = f.TripAverageMpg, TripMiles = f.TripMiles,
            Range = f.Range, Odometer = f.Odometer, Notes = f.Notes,
            AddedDate = f.AddedDate, AddedBy = f.AddedBy, ModifiedDate = f.ModifiedDate, ModifiedBy = f.ModifiedBy
        }).ToListAsync(),
        FuelEntryStops = await db.FuelEntryStops.OrderBy(s => s.FuelEntryId).ThenBy(s => s.Sequence).Select(s => new FuelEntryStopBackup
        {
            FuelEntryId = s.FuelEntryId, Sequence = s.Sequence, FuelStationId = s.FuelStationId,
            AddedDate = s.AddedDate, AddedBy = s.AddedBy, ModifiedDate = s.ModifiedDate, ModifiedBy = s.ModifiedBy
        }).ToListAsync()
    };

    /// <summary>
    /// Replaces all the user's data with the backup's, in one transaction: if anything fails,
    /// nothing changes. Rows get new ids; their audit columns are kept from the backup.
    /// </summary>
    public async Task RestoreAsync(BackupFile backup)
    {
        // The retrying execution strategy needs the whole transaction inside it, so it can be replayed as one unit.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();

            // The query filters limit these to the current user's rows. Stops go first, as they
            // stop their stations being deleted, and fill-ups before their vehicles.
            await db.FuelEntryStops.ExecuteDeleteAsync();
            await db.FuelEntries.ExecuteDeleteAsync();
            await db.Vehicles.ExecuteDeleteAsync();
            await db.Owners.ExecuteDeleteAsync();
            await db.FuelStations.ExecuteDeleteAsync();
            await db.FuelBrands.ExecuteDeleteAsync();

            AddRows(backup);
            db.KeepAuditValues = true;
            try { await db.SaveChangesAsync(); }
            finally { db.KeepAuditValues = false; }

            await transaction.CommitAsync();
        });
        db.ChangeTracker.Clear();
    }

    private void AddRows(BackupFile backup)
    {
        var owners = backup.Owners.ToDictionary(o => o.Id, o => Audited(o, new Owner { Name = o.Name }));
        var vehicles = backup.Vehicles.ToDictionary(v => v.Id, v => Audited(v, new Vehicle
        {
            Owner = owners[v.OwnerId], Type = v.Type, FuelType = v.FuelType, Make = v.Make, Model = v.Model,
            RegistrationNumber = v.RegistrationNumber, RegistrationDate = v.RegistrationDate, PurchasedDate = v.PurchasedDate
        }));
        var brands = backup.FuelBrands.ToDictionary(b => b.Id, b => Audited(b, new FuelBrand { Name = b.Name }));
        var stations = backup.FuelStations.ToDictionary(s => s.Id, s => Audited(s, new FuelStation
        {
            FuelBrand = brands[s.FuelBrandId], Name = s.Name
        }));
        var entries = backup.FuelEntries.ToDictionary(f => f.Id, f => Audited(f, new FuelEntry
        {
            Vehicle = vehicles[f.VehicleId], Date = f.Date, Litres = f.Litres, CostPerLitre = f.CostPerLitre,
            VoucherPerLitre = f.VoucherPerLitre, TripAverageMpg = f.TripAverageMpg, TripMiles = f.TripMiles,
            Range = f.Range, Odometer = f.Odometer, Notes = f.Notes
        }));
        foreach (var s in backup.FuelEntryStops)
            entries[s.FuelEntryId].Stops.Add(Audited(s, new FuelEntryStop { Sequence = s.Sequence, FuelStation = stations[s.FuelStationId] }));

        db.AddRange(owners.Values);
        db.AddRange(vehicles.Values);
        db.AddRange(brands.Values);
        db.AddRange(stations.Values);
        db.AddRange(entries.Values);
    }

    private static T Audited<T>(BackupRow row, T entity) where T : AuditedEntity
    {
        entity.AddedDate = row.AddedDate;
        entity.AddedBy = row.AddedBy;
        entity.ModifiedDate = row.ModifiedDate;
        entity.ModifiedBy = row.ModifiedBy;
        return entity;
    }
}
