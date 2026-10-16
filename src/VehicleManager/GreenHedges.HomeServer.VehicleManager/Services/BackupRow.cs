namespace VehicleManager.Services;

/// <summary>The audit columns every table has. Restoring keeps them; missing ones are filled in as for new rows.</summary>
public abstract record BackupRow
{
    public DateTime AddedDate { get; init; }
    public string AddedBy { get; init; } = string.Empty;
    public DateTime ModifiedDate { get; init; }
    public string ModifiedBy { get; init; } = string.Empty;
}
