using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// Identifies a reminder, so it's sent once: a due date, or for a service due by mileage, the mileage
/// (its estimated date moves with each fill-up, so it isn't part of the key).
/// </summary>
public sealed record ReminderKey(int VehicleId, DueDateKind Kind, DateOnly? DueDate, int? DueMileage, int DaysBefore)
{
    public static ReminderKey For(int vehicleId, DueDateKind kind, DateOnly dueDate, int? dueMileage, int daysBefore) =>
        new(vehicleId, kind, dueMileage is null ? dueDate : null, dueMileage, daysBefore);

    public static ReminderKey For(DueDateView due, int daysBefore) => For(due.VehicleId, due.Kind, due.Date, due.DueMileage, daysBefore);
}
