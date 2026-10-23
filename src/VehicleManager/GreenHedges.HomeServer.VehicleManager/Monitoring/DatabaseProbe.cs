using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>Probes the database in a scope of its own, giving up after <see cref="Timeout"/>.</summary>
public sealed class DatabaseProbe(IServiceScopeFactory scopes) : IDatabaseProbe
{
    /// <summary>The retrying execution strategy would otherwise keep trying for a while when the database is down.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Database.CanConnectAsync(timeout.Token) ? null : "The database didn't accept a connection.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"The database didn't answer within {Timeout.TotalSeconds:0} seconds.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
