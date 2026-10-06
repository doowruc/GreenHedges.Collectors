using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VehicleManager.Data;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies any pending EF Core migrations, creating the database if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// Databases created before migrations were introduced (via <c>EnsureCreated</c>) already
    /// have the tables but no migrations history. Those are "adopted" by recording the initial
    /// migration as applied, so later migrations apply on top without recreating the tables.
    /// </remarks>
    public static void MigrateDatabase(this AppDbContext db, ILogger logger)
    {
        var initialMigration = db.Database.GetMigrations().First();

        db.Database.OpenConnection();
        try
        {
            if (TableExists(db, "Vehicles") && !TableExists(db, HistoryRepository.DefaultTableName))
            {
                logger.LogInformation(
                    "Existing database has no migrations history – marking {Migration} as applied.", initialMigration);

                var history = db.GetService<IHistoryRepository>();
                var efVersion = typeof(DbContext).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                    .InformationalVersion.Split('+')[0];

                db.Database.ExecuteSqlRaw(history.GetCreateScript());
                db.Database.ExecuteSqlRaw(history.GetInsertScript(new HistoryRow(initialMigration, efVersion)));
            }
        }
        finally
        {
            db.Database.CloseConnection();
        }

        db.Database.Migrate();
    }

    private static bool TableExists(AppDbContext db, string table) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {table}")
            .Single() > 0;
}
