using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class AppFactoryTests(AppFactory app) : IClassFixture<AppFactory>
{
    [DatabaseFact]
    public void The_test_host_doesnt_apply_migrations_to_the_real_database() =>
        Assert.False(app.Services.GetRequiredService<IConfiguration>().GetValue("MigrateOnStartup", true));
}
