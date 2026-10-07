using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>
/// The nightly backup (scheduled in <c>Program.cs</c>): emails each user with a backup email address
/// a backup, if their data has changed since their last scheduled backup.
/// </summary>
public class ScheduledBackups(AppDbContext db, IServiceScopeFactory scopes, ILogger<ScheduledBackups> logger) : IInvocable
{
    public async Task Invoke()
    {
        foreach (var userId in await UsersToBackUpAsync())
        {
            try
            {
                var result = await BackUpAsync(userId);
                logger.LogInformation("Scheduled backup for {UserId}: {Result}", userId, result);
            }
            // One user's failure (e.g. Resend refusing their address) shouldn't stop the others' backups.
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled backup for {UserId} failed", userId);
            }
        }
    }

    /// <summary>Everyone with a backup email address. Deliberately across all users, so it bypasses the query filters.</summary>
    public Task<List<string>> UsersToBackUpAsync() =>
        db.UserSettings.IgnoreQueryFilters()
            .Where(s => s.BackupEmail != null && s.BackupEmail != "")
            .Select(s => s.UserId)
            .ToListAsync();

    /// <summary>Emails one user's backup if it has changed, acting as that user in a scope of its own.</summary>
    public async Task<BackupEmailResult> BackUpAsync(string userId)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(userId);
        return await scope.ServiceProvider.GetRequiredService<BackupEmailer>().EmailAsync(scheduled: true);
    }
}
