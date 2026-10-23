using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Collectors.Weather.Storage;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Backups;

/// <summary>Backs up the weather archive.</summary>
public sealed class WeatherArchiveBackupSource(WeatherArchive archive) : IBackupSource
{
    public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) =>
        archive.BackupToAsync(destinationPath, cancellationToken);
}
