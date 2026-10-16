namespace VehicleManager.Services;

public enum BackupEmailResult
{
    /// <summary>The user hasn't set a backup email address.</summary>
    NoAddress,
    /// <summary>Nothing has changed since the last scheduled backup, so none was sent.</summary>
    Unchanged,
    Sent
}
