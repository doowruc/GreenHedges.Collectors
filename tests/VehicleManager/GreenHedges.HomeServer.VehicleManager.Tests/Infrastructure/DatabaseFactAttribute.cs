namespace VehicleManager.Tests.Infrastructure;

/// <summary>A test that needs the database; skipped when no connection string is configured.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (TestDatabase.ConnectionString is null)
            Skip = "No database configured: set ConnectionStrings:VehicleManager in the app's user secrets.";
    }
}
