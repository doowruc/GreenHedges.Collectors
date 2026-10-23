namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>A test that needs the local SQL Server and its shared backups folder; skipped without them.</summary>
public sealed class SqlServerBackupFactAttribute : FactAttribute
{
    public SqlServerBackupFactAttribute()
    {
        if (!TestDatabase.IsConfigured)
            Skip = "No SQL Server for the tests: set ConnectionStrings:VehicleManagerTests (see the README).";
        else if (SqlServerBackupFolders.Local is null)
            Skip = "No shared backups folder: run the local SQL Server from compose.yaml (it creates .sqlserver-backups/).";
    }
}
