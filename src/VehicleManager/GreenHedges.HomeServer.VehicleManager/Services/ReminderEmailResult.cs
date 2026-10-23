namespace GreenHedges.HomeServer.VehicleManager.Services;

public enum ReminderEmailResult
{
    /// <summary>The user hasn't set a reminder email address.</summary>
    NoAddress,
    /// <summary>No reminders were due that hadn't already been sent.</summary>
    NothingDue,
    Sent
}
