using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>
/// Hands data from before per-user data existed (owners and vehicles with no user id) to one
/// person: the user whose verified email matches the <c>ExistingDataOwner</c> setting, the first
/// time they sign in. Their fill-ups follow their vehicles. Once claimed the setting does nothing,
/// so it can be removed.
/// </summary>
public class ExistingDataClaim(AppDbContext db, AllowedUsers allowedUsers, IConfiguration config, ILogger<ExistingDataClaim> logger)
{
    public async Task ClaimIfOwnerAsync(ClaimsPrincipal user)
    {
        if (config["ExistingDataOwner"] is not { Length: > 0 } ownerEmail) return;
        if (!allowedUsers.IsAllowed(user) ||
            !string.Equals(AllowedUsers.Email(user), ownerEmail.Trim(), StringComparison.OrdinalIgnoreCase) ||
            CurrentUser.IdOf(user) is not { } userId)
            return;

        // Unclaimed rows belong to no user, so the usual per-user query filters must be bypassed.
        var owners = db.Owners.IgnoreQueryFilters().Where(o => o.UserId == "");
        var vehicles = db.Vehicles.IgnoreQueryFilters().Where(v => v.UserId == "");
        if (!await owners.AnyAsync() && !await vehicles.AnyAsync()) return;

        await using var transaction = await db.Database.BeginTransactionAsync();
        var ownerCount = await owners.ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, userId));
        var vehicleCount = await vehicles.ExecuteUpdateAsync(s => s.SetProperty(v => v.UserId, userId));
        await transaction.CommitAsync();

        logger.LogInformation("Gave {Owners} existing owner(s) and {Vehicles} vehicle(s) to {Email}.",
            ownerCount, vehicleCount, ownerEmail);
    }
}
