using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Collectors.Libre.Storage;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Backups;

/// <summary>Backs up the glucose archive.</summary>
public sealed class ArchiveBackupSource(GlucoseArchive archive) : IBackupSource
{
    public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) =>
        archive.BackupToAsync(destinationPath, cancellationToken);
}
