namespace VehicleManager.Tests.Infrastructure;

/// <summary>A theory that needs the database; skipped when no connection string is configured.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (TestDatabase.ConnectionString is null)
            Skip = "No database configured: set ConnectionStrings:VehicleManager in the app's user secrets.";
    }
}
