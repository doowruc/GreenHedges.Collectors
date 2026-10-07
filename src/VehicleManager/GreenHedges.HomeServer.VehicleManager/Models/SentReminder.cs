namespace VehicleManager.Models;

/// <summary>Something that falls due for a vehicle: what <c>Services/Reminders</c> emails about.</summary>
public enum DueDateKind
{
    Mot,
    Tax,
    Insurance,
    Service
}

/// <summary>
/// A reminder email already sent: <see cref="DaysBefore"/> days before <see cref="DueDate"/>, for one of a
/// vehicle's due dates. It stops the same reminder being sent twice; a changed due date gets reminders of its own.
/// Bookkeeping only, so not part of a backup. Belongs to the user who owns the vehicle, and goes with it.
/// </summary>
public class SentReminder : AuditedEntity
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DueDateKind Kind { get; set; }
    public DateOnly DueDate { get; set; }
    public int DaysBefore { get; set; }
}
