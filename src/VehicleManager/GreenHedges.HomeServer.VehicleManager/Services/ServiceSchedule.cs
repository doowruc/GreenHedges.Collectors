using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Services;

/// <summary>
/// Works out when a vehicle's next service is due from its latest one and its service interval
/// (<see cref="Vehicle.ServiceIntervalMonths"/>, <see cref="Vehicle.ServiceIntervalMiles"/>).
/// </summary>
public static class ServiceSchedule
{
    /// <summary>The next service after one on <paramref name="date"/> at <paramref name="mileage"/>; null where there's no interval.</summary>
    public static (DateOnly? Date, int? Mileage) NextAfter(DateOnly date, int mileage, int? intervalMonths, int? intervalMiles) =>
        (intervalMonths is { } months ? date.AddMonths(months) : null, intervalMiles is { } miles ? mileage + miles : null);

    /// <summary>
    /// Sets when each service was due: the interval after the one before it (by date). The first has no due date or mileage.
    /// </summary>
    public static void SetDue(IEnumerable<ServiceRecordView> services, int? intervalMonths, int? intervalMiles)
    {
        ServiceRecordView? previous = null;
        foreach (var service in services.OrderBy(s => s.Date).ThenBy(s => s.Id))
        {
            (service.DueDate, service.DueMileage) = previous is null
                ? (null, null)
                : NextAfter(previous.Date, previous.Mileage, intervalMonths, intervalMiles);
            previous = service;
        }
    }

    /// <summary>How late <paramref name="done"/> was after <paramref name="due"/>: whole months ("2 months late"), or days under a month.</summary>
    public static string LateBy(DateOnly due, DateOnly done)
    {
        var months = (done.Year - due.Year) * 12 + done.Month - due.Month - (done.Day < due.Day ? 1 : 0);
        var days = done.DayNumber - due.DayNumber;
        return months >= 1 ? $"{months} month{(months == 1 ? "" : "s")} late" : $"{days} day{(days == 1 ? "" : "s")} late";
    }

    /// <summary>
    /// After <paramref name="service"/> is added or changed (not yet saved): if it's the vehicle's latest service, sets
    /// the vehicle's next service due date and mileage from it and the intervals. One without an interval is left as it is.
    /// </summary>
    /// <returns>What was set, for showing to the user (e.g. "Next service due 08/10/2027 or at 55,000 miles."); null if it isn't the latest.</returns>
    public static async Task<string?> UpdateNextDueAsync(AppDbContext db, Vehicle vehicle, ServiceRecord service)
    {
        // Ties go to the one being saved, so a second service on the same day still counts as the latest.
        if (await db.ServiceRecords.AnyAsync(s => s.VehicleId == vehicle.Id && s.Id != service.Id && s.Date > service.Date))
            return null;

        var (date, mileage) = NextAfter(service.Date, service.Mileage, vehicle.ServiceIntervalMonths, vehicle.ServiceIntervalMiles);
        if (date is not null) vehicle.ServiceDueDate = date;
        if (mileage is not null) vehicle.ServiceDueMileage = mileage;

        return (date, mileage) switch
        {
            (null, null) => "Set a service interval on the vehicle to have the next service worked out.",
            ({ } d, null) => $"Next service due {d:dd/MM/yyyy}.",
            (null, { } m) => $"Next service due at {m:N0} miles.",
            ({ } d, { } m) => $"Next service due {d:dd/MM/yyyy} or at {m:N0} miles."
        };
    }
}
