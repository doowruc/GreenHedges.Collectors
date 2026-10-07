using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.ViewModels;

/// <summary>One of a vehicle's due dates (MOT, tax, insurance, service), and how soon it is.</summary>
public sealed record DueDateView(int VehicleId, string VehicleName, string RegistrationNumber, DueDateKind Kind, DateOnly Date, DateOnly Today)
{
    public string Name => Reminders.Name(Kind);

    /// <summary>Days until it's due; negative once it's overdue.</summary>
    public int DaysLeft => Date.DayNumber - Today.DayNumber;

    /// <summary>Due within <see cref="Reminders.SoonDays"/> days, or overdue.</summary>
    public bool IsSoon => DaysLeft <= Reminders.SoonDays;

    /// <summary>A Bootstrap badge class (theme-aware): red when overdue, amber within a week, blue within a month.</summary>
    public string BadgeClass => DaysLeft switch
    {
        < 0 => "text-bg-danger",
        <= 7 => "text-bg-warning",
        <= Reminders.SoonDays => "text-bg-info",
        _ => "text-bg-secondary"
    };

    public string When => Reminders.When(Date, Today);

    public static List<DueDateView> For(VehicleView v, DateOnly today) =>
        new (DueDateKind Kind, DateOnly? Date)[]
            {
                (DueDateKind.Mot, v.MotDueDate), (DueDateKind.Tax, v.TaxDueDate),
                (DueDateKind.Insurance, v.InsuranceDueDate), (DueDateKind.Service, v.ServiceDueDate)
            }
            .Where(d => d.Date is not null)
            .Select(d => new DueDateView(v.Id, v.DisplayName, v.RegistrationNumber, d.Kind, d.Date!.Value, today))
            .OrderBy(d => d.Date).ThenBy(d => d.Kind)
            .ToList();
}
