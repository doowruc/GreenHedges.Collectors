namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>
/// The folder the local SQL Server (<c>compose.yaml</c>) shares with this machine for backups: <c>.sqlserver-backups/</c>
/// at the repo root, <c>/var/opt/mssql/backups</c> inside the container. Found from the test's location; null when it
/// doesn't exist (e.g. in CI, whose SQL Server container shares no folder).
/// </summary>
public sealed record SqlServerBackupFolders(string Server, string Host)
{
    public static SqlServerBackupFolders? Local { get; } = Find();

    private static SqlServerBackupFolders? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "GreenHedges.HomeServer.slnx"))) continue;
            var host = Path.Combine(dir.FullName, ".sqlserver-backups");
            return Directory.Exists(host) ? new SqlServerBackupFolders("/var/opt/mssql/backups", host) : null;
        }

        return null;
    }
}
