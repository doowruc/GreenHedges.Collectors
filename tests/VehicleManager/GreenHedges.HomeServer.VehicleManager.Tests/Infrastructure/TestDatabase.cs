using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VehicleManager.Data;
using VehicleManager.Services;

namespace VehicleManager.Tests.Infrastructure;

/// <summary>
/// The tests' access to the app's database, using the connection string from the app's user
/// secrets. It's the real database, so tests never touch real data: each test acts as its own
/// throwaway user (<c>test|…</c>), which the app's per-user query filters keep apart from
/// everyone else's data, and deletes everything that user owns afterwards.
/// </summary>
public static class TestDatabase
{
    public const string UserPrefix = "test|";

    public static string? ConnectionString { get; } = new ConfigurationBuilder()
        .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build()
        .GetConnectionString("VehicleManager");

    private static readonly DbContextOptions<AppDbContext> Options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(ConnectionString ?? "Server=not-configured", sql => sql.EnableRetryOnFailure())
        .Options;

    public static string NewUserId() => $"{UserPrefix}{Guid.NewGuid():N}";

    /// <summary>A context acting as <paramref name="userId"/> (null: no signed-in user).</summary>
    public static AppDbContext As(string? userId) => new(Options, new TestCurrentUser(userId));

    /// <summary>Deletes everything a test user owns. Refuses anything but a test user.</summary>
    public static async Task DeleteUserDataAsync(string userId)
    {
        if (!userId.StartsWith(UserPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Only test users ({UserPrefix}…) can be deleted.", nameof(userId));

        await using var db = As(null);
        await db.FuelEntryStops.IgnoreQueryFilters().Where(s => s.FuelEntry!.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.FuelEntries.IgnoreQueryFilters().Where(f => f.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.Vehicles.IgnoreQueryFilters().Where(v => v.UserId == userId).ExecuteDeleteAsync();
        await db.Owners.IgnoreQueryFilters().Where(o => o.UserId == userId).ExecuteDeleteAsync();
        await db.FuelStations.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
        await db.FuelBrands.IgnoreQueryFilters().Where(b => b.UserId == userId).ExecuteDeleteAsync();
        await db.UserSettings.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
    }
}

/// <summary>Acts as a fixed user instead of reading the signed-in user from the request.</summary>
public class TestCurrentUser(string? id) : CurrentUser(new Microsoft.AspNetCore.Http.HttpContextAccessor())
{
    public override string? Id => id;
}
