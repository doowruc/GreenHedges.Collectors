using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's
/// owners, vehicles and (through their vehicle) fill-ups, and <see cref="SaveChangesAsync(bool, CancellationToken)"/>
/// stamps new owners and vehicles with the current user's id. With no signed-in user (e.g. at startup)
/// queries return nothing. Use <c>IgnoreQueryFilters()</c> only for deliberate cross-user work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null) : DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();

    /// <summary>Read by the query filters; EF Core evaluates it per query, as a parameter.</summary>
    private string? CurrentUserId => currentUser?.Id;

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
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampNewRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampNewRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampNewRows()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.UserId = CurrentUserId
                ?? throw new InvalidOperationException("Can't save user data without a signed-in user.");
        }
    }
}
