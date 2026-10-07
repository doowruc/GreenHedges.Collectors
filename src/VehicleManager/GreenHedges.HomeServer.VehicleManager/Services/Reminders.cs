using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Services;

/// <summary>
/// When to remind about a vehicle's due dates: <see cref="DaysBefore"/> days before each one (and once,
/// if a date is first seen when it's already closer than that, or overdue).
/// </summary>
public static class Reminders
{
    /// <summary>Due dates this close (or overdue) are "coming up" on the dashboard.</summary>
    public const int SoonDays = 30;

    /// <summary>How many days before a due date reminders are sent, furthest first.</summary>
    public static readonly IReadOnlyList<int> DaysBefore = [30, 7];

    private static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>Today's date in the UK, whatever the server's time zone.</summary>
    public static DateOnly UkToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, UkTime));

    public static string Name(DueDateKind kind) => kind switch
    {
        DueDateKind.Mot => "MOT",
        DueDateKind.Tax => "Tax",
        DueDateKind.Insurance => "Insurance",
        DueDateKind.Service => "Service",
        _ => kind.ToString()
    };

    /// <summary>How soon <paramref name="date"/> is: "in 5 days", "today", "2 days overdue"…</summary>
    public static string When(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        < -1 and var d => $"{-d} days overdue",
        -1 => "1 day overdue",
        0 => "today",
        1 => "tomorrow",
        var d => $"in {d} days"
    };

    /// <summary>The reminder a due date has reached today: the nearest of <see cref="DaysBefore"/> it's within, or null if it's further off.</summary>
    public static int? DaysBeforeFor(DateOnly due, DateOnly today)
    {
        var daysLeft = due.DayNumber - today.DayNumber;
        int? reached = null;
        foreach (var days in DaysBefore)
            if (daysLeft <= days) reached = days;
        return reached;
    }

    /// <summary>The reminders to send today for <paramref name="dueDates"/>, leaving out any already <paramref name="sent"/>.</summary>
    public static List<(DueDateView Due, int DaysBefore)> ToSend(
        IEnumerable<DueDateView> dueDates, ISet<(int VehicleId, DueDateKind Kind, DateOnly DueDate, int DaysBefore)> sent) =>
        dueDates
            .Select(d => (Due: d, DaysBefore: DaysBeforeFor(d.Date, d.Today)))
            .Where(r => r.DaysBefore is { } days && !sent.Contains((r.Due.VehicleId, r.Due.Kind, r.Due.Date, days)))
            .Select(r => (r.Due, r.DaysBefore!.Value))
            .OrderBy(r => r.Due.Date).ThenBy(r => r.Due.VehicleName).ThenBy(r => r.Due.Kind)
            .ToList();

    /// <summary>One line of a reminder email, e.g. "MOT for Ford Focus (AB12 CDE) is due on 06/11/2026 (in 30 days)."</summary>
    public static string Describe(DueDateView due) =>
        $"{due.Name} for {due.VehicleName} ({due.RegistrationNumber}) " +
        (due.DaysLeft < 0 ? "was" : "is") + $" due on {due.Date:dd/MM/yyyy} ({due.When}).";
}
