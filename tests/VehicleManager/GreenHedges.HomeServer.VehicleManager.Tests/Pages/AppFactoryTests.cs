using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class AppFactoryTests(AppFactory app) : IClassFixture<AppFactory>
{
    [DatabaseFact]
    public void The_test_host_doesnt_migrate_at_startup() =>
        Assert.False(app.Services.GetRequiredService<IConfiguration>().GetValue("MigrateOnStartup", true));

    [DatabaseFact]
    public void The_app_under_test_uses_the_throwaway_test_database_not_its_own()
    {
        using var scope = app.Services.CreateScope();
        var connection = new SqlConnectionStringBuilder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString());

        Assert.StartsWith(TestDatabase.DatabasePrefix, connection.InitialCatalog);
        Assert.Equal(new SqlConnectionStringBuilder(TestDatabase.ConnectionString).InitialCatalog, connection.InitialCatalog);
        Assert.DoesNotContain(".database.windows.net", connection.DataSource, StringComparison.OrdinalIgnoreCase);
    }
}
