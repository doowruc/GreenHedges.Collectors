using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VehicleManager.Data;

/// <summary>
/// Lets <c>dotnet ef</c> create the context without running <c>Program.cs</c>, which needs the
/// Auth0 settings. Only used for designing migrations, never at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=vehiclemanager.db").Options);
}
