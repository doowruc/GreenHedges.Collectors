namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>Something that falls due for a vehicle: what <c>Services/Reminders</c> emails about.</summary>
public enum DueDateKind
{
    Mot,
    Tax,
    Insurance,
    Service
}
