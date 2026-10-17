using System.Text.Json;

namespace GreenHedges.Collectors.Common.Alerts;

/// <summary>An open alert as saved between restarts.</summary>
/// <param name="Since">When the problem started.</param>
/// <param name="Notified">Whether the PROBLEM email went out (so a RESOLVED one is owed).</param>
public sealed record PersistedAlert(string Title, DateTimeOffset Since, bool Notified);

/// <summary>
/// Saves open alerts to a small JSON file so a restart neither repeats a PROBLEM email nor loses the
/// RESOLVED one. Operational state only; the readings archive is unaffected. Writes are atomic
/// (temp file, then rename), and a missing or unreadable file means "no open alerts".
/// </summary>
public sealed class AlertStateStore(string path, ILogger<AlertStateStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path { get; } = path;

    public async Task<IReadOnlyDictionary<string, PersistedAlert>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new Dictionary<string, PersistedAlert>();
            }

            await using var stream = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, PersistedAlert>>(stream, JsonOptions, cancellationToken)
                ?? new Dictionary<string, PersistedAlert>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Couldn't read alert state from {Path}; starting with no open alerts", Path);
            return new Dictionary<string, PersistedAlert>();
        }
    }

    /// <summary>Saves the open alerts. Never throws: failing to save only risks a repeat or missed email after a restart.</summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, PersistedAlert> alerts, CancellationToken cancellationToken)
    {
        var temp = Path + ".tmp";
        try
        {
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, alerts, JsonOptions, cancellationToken);
            }

            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't save alert state to {Path}", Path);
        }
    }
}
