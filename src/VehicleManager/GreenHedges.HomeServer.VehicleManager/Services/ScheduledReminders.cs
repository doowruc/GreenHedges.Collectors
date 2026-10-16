using System.Diagnostics;
using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>
/// Each morning (scheduled in <c>Program.cs</c>), for every user with vehicles: checks their MOT and tax
/// due dates with whichever of the DVLA and DVSA are set up, then emails any reminders due (if they've set an address).
/// </summary>
public class ScheduledReminders(AppDbContext db, IServiceScopeFactory scopes, ILogger<ScheduledReminders> logger) : IInvocable
{
    public async Task Invoke()
    {
        var today = Reminders.UkToday();
        // A trace per run with a span per user (Telemetry), so the database, email and lookup calls for each user sit under it.
        using var run = Telemetry.Source.StartActivity("Scheduled reminders");
        var users = await UsersAsync();
        run?.SetTag("vehiclemanager.users", users.Count);
        foreach (var userId in users)
        {
            using var span = Telemetry.Source.StartActivity("Remind user");
            span?.SetTag("enduser.id", userId);
            try
            {
                var result = await RunForAsync(userId, today);
                span?.SetTag("vehiclemanager.vehicles_updated", result.VehiclesUpdated)
                    .SetTag("vehiclemanager.lookup_problems", result.LookupProblems)
                    .SetTag("vehiclemanager.email_result", result.Email.ToString());
                logger.LogInformation("Reminders for {UserId}: {Result}", userId, result);
            }
            // One user's failure (e.g. Resend refusing their address) shouldn't stop the others' reminders.
            catch (Exception ex)
            {
                span?.AddException(ex).SetStatus(ActivityStatusCode.Error);
                logger.LogError(ex, "Reminders for {UserId} failed", userId);
            }
        }
    }

    /// <summary>Everyone with vehicles. Deliberately across all users, so it bypasses the query filters.</summary>
    public Task<List<string>> UsersAsync() =>
        db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.UserId != "")
            .Select(v => v.UserId)
            .Distinct()
            .ToListAsync();

    /// <summary>Updates one user's MOT and tax dates and emails their reminders, acting as that user in a scope of its own.</summary>
    public async Task<ReminderRunResult> RunForAsync(string userId, DateOnly today)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<CurrentUser>().ActAs(userId);
        var userDb = services.GetRequiredService<AppDbContext>();
        var lookup = services.GetRequiredService<VehicleLookup>();

        int updated = 0, problems = 0;
        if (lookup.IsAvailable)
        {
            foreach (var vehicle in await userDb.Vehicles.OrderBy(v => v.Id).ToListAsync())
            {
                problems += (await lookup.RefreshAsync(vehicle)).Problems.Count;
                if (userDb.Entry(vehicle).State == EntityState.Modified) updated++;
            }
            await userDb.SaveChangesAsync();
        }

        var email = await services.GetRequiredService<ReminderEmailer>().EmailAsync(today);
        return new(updated, problems, email);
    }
}
