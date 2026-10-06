using Microsoft.EntityFrameworkCore;
using VehicleManager.Models;

namespace VehicleManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>(e =>
        {
            e.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(v => v.RegistrationNumber).IsUnique();
            e.HasMany(v => v.FuelEntries)
             .WithOne(f => f.Vehicle)
             .HasForeignKey(f => f.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FuelEntry>(e =>
        {
            e.HasIndex(f => new { f.VehicleId, f.Date });
            e.Property(f => f.Litres).HasPrecision(8, 3);
            e.Property(f => f.CostPerLitre).HasPrecision(6, 3);
            e.Property(f => f.VoucherPerLitre).HasPrecision(6, 3);
            e.Property(f => f.TripAverageMpg).HasPrecision(5, 1);
            e.Property(f => f.TripMiles).HasPrecision(7, 1);
        });
    }
}
