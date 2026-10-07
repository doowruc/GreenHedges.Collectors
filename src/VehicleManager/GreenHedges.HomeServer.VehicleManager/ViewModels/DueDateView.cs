using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.ViewModels;

/// <summary>
/// One of a vehicle's due dates (MOT, tax, insurance, service), and how soon it is. A service due by
/// mileage has <see cref="DueMileage"/> set, and <see cref="Date"/> is estimated from the recent miles per month.
/// </summary>
public sealed record DueDateView(int VehicleId, string VehicleName, string RegistrationNumber, DueDateKind Kind, DateOnly Date, DateOnly Today)
{
    /// <summary>For a service due by mileage (sooner than by date): the mileage. <see cref="Date"/> is then an estimate.</summary>
    public int? DueMileage { get; init; }

    /// <summary>The recent miles per month the estimate is based on.</summary>
    public double? MilesPerMonth { get; init; }

    public bool IsEstimate => DueMileage is not null;

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

    /// <summary>The date, marked "≈" when it's estimated from the mileage.</summary>
    public string DateText => (IsEstimate ? "≈" : "") + Date.ToString("dd/MM/yyyy");

    /// <summary>For an estimate, e.g. "at 45,000 miles, estimated from about 1,050 miles a month"; otherwise null.</summary>
    public string? Detail => DueMileage is { } miles
        ? $"at {miles:N0} miles, estimated from about {MilesPerMonth:N0} miles a month"
        : null;

    public static List<DueDateView> For(VehicleView v, DateOnly today, MileageTrend? mileage = null)
    {
        DueDateView Due(DueDateKind kind, DateOnly date) => new(v.Id, v.DisplayName, v.RegistrationNumber, kind, date, today);

        var dueDates = new (DueDateKind Kind, DateOnly? Date)[]
            {
                (DueDateKind.Mot, v.MotDueDate), (DueDateKind.Tax, v.TaxDueDate), (DueDateKind.Insurance, v.InsuranceDueDate)
            }
            .Where(d => d.Date is not null)
            .Select(d => Due(d.Kind, d.Date!.Value))
            .ToList();

        // A service is due by date or mileage, whichever comes first.
        var byMileage = v.ServiceDueMileage is { } miles && mileage is not null ? mileage.DateAt(miles) : (DateOnly?)null;
        if (byMileage is { } estimated && (v.ServiceDueDate is null || estimated < v.ServiceDueDate))
            dueDates.Add(Due(DueDateKind.Service, estimated) with { DueMileage = v.ServiceDueMileage, MilesPerMonth = mileage!.MilesPerMonth });
        else if (v.ServiceDueDate is { } date)
            dueDates.Add(Due(DueDateKind.Service, date));

        return dueDates.OrderBy(d => d.Date).ThenBy(d => d.Kind).ToList();
    }
}
