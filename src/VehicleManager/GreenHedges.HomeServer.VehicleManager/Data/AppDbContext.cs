using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's
/// owners, vehicles, fuel brands and stations, settings, and (through their vehicle) fill-ups and their stops.
/// <see cref="SaveChangesAsync(bool, CancellationToken)"/> stamps new user-owned rows with the
/// current user's id and fills in every table's audit columns.
/// With no signed-in user (e.g. at startup) queries return nothing and saving throws. Use
/// <c>IgnoreQueryFilters()</c> only for deliberate cross-user work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null) : DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();
    public DbSet<FuelEntryStop> FuelEntryStops => Set<FuelEntryStop>();
    public DbSet<FuelBrand> FuelBrands => Set<FuelBrand>();
    public DbSet<FuelStation> FuelStations => Set<FuelStation>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    /// <summary>Read by the query filters; EF Core evaluates it per query, as a parameter.</summary>
    private string? CurrentUserId => currentUser?.Id;

    /// <summary>
    /// When set, new rows keep the audit values they already have (a restored backup's), and only
    /// missing ones are filled in. New user-owned rows still always belong to the current user.
    /// </summary>
    public bool KeepAuditValues { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Owner>(e =>
        {
            e.HasQueryFilter(o => o.UserId == CurrentUserId);
            e.HasIndex(o => new { o.UserId, o.Name }).IsUnique();
            e.HasMany(o => o.Vehicles)
             .WithOne(v => v.Owner)
             .HasForeignKey(v => v.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            e.HasQueryFilter(v => v.UserId == CurrentUserId);
            e.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(v => new { v.UserId, v.RegistrationNumber }).IsUnique();
            e.HasMany(v => v.FuelEntries)
             .WithOne(f => f.Vehicle)
             .HasForeignKey(f => f.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FuelEntry>(e =>
        {
            e.HasQueryFilter(f => f.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(f => new { f.VehicleId, f.Date });
            e.Property(f => f.Litres).HasPrecision(8, 3);
            e.Property(f => f.CostPerLitre).HasPrecision(6, 3);
            e.Property(f => f.VoucherPerLitre).HasPrecision(6, 3);
            e.Property(f => f.TripAverageMpg).HasPrecision(5, 1);
            e.Property(f => f.TripMiles).HasPrecision(7, 1);
            e.HasMany(f => f.Stops)
             .WithOne(s => s.FuelEntry)
             .HasForeignKey(s => s.FuelEntryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FuelEntryStop>(e =>
        {
            e.HasQueryFilter(s => s.FuelEntry!.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(s => new { s.FuelEntryId, s.Sequence }).IsUnique();
        });

        // Brands with stations, and stations in use, can't be deleted; merge them instead (Pages/Places).
        modelBuilder.Entity<FuelBrand>(e =>
        {
            e.HasQueryFilter(b => b.UserId == CurrentUserId);
            e.HasIndex(b => new { b.UserId, b.Name }).IsUnique();
            e.HasMany(b => b.Stations)
             .WithOne(s => s.FuelBrand)
             .HasForeignKey(s => s.FuelBrandId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FuelStation>(e =>
        {
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
            e.HasIndex(s => new { s.FuelBrandId, s.Name }).IsUnique();
            e.HasMany(s => s.Stops)
             .WithOne(st => st.FuelStation)
             .HasForeignKey(st => st.FuelStationId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSettings>(e =>
        {
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
            e.HasIndex(s => s.UserId).IsUnique();
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills in the audit columns, and the owning user on new rows.</summary>
    private void StampChanges()
    {
        var entries = ChangeTracker.Entries<AuditedEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
        if (entries.Count == 0) return;

        var userId = CurrentUserId
            ?? throw new InvalidOperationException("Can't save data without a signed-in user.");
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            var audit = entry.Entity;
            var keep = KeepAuditValues && entry.State == EntityState.Added;
            if (entry.State == EntityState.Added)
            {
                if (!keep || audit.AddedDate == default) audit.AddedDate = now;
                if (!keep || audit.AddedBy.Length == 0) audit.AddedBy = userId;
                if (audit is IUserOwned owned) owned.UserId = userId;
            }
            else
            {
                // Who added a row, and when, never changes.
                entry.Property(e => e.AddedDate).IsModified = false;
                entry.Property(e => e.AddedBy).IsModified = false;
            }
            if (!keep || audit.ModifiedDate == default) audit.ModifiedDate = now;
            if (!keep || audit.ModifiedBy.Length == 0) audit.ModifiedBy = userId;
        }
    }
}
