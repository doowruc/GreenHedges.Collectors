using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Tests.Unit;

public class RemindersTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static DueDateView Due(int daysLeft, DueDateKind kind = DueDateKind.Mot, int vehicleId = 1) =>
        new(vehicleId, "Ford Focus", "AB12 CDE", kind, Today.AddDays(daysLeft), Today);

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, 30)]
    [InlineData(8, 30)]
    [InlineData(7, 7)]
    [InlineData(0, 7)]
    [InlineData(-10, 7)]
    public void A_due_date_reaches_the_nearest_reminder_its_within(int daysLeft, int? expected) =>
        Assert.Equal(expected, Reminders.DaysBeforeFor(Today.AddDays(daysLeft), Today));

    [Theory]
    [InlineData(10, "in 10 days")]
    [InlineData(1, "tomorrow")]
    [InlineData(0, "today")]
    [InlineData(-1, "1 day overdue")]
    [InlineData(-3, "3 days overdue")]
    public void Says_how_soon_a_date_is(int daysLeft, string expected) =>
        Assert.Equal(expected, Reminders.When(Today.AddDays(daysLeft), Today));

    [Fact]
    public void Sends_each_reminder_once()
    {
        var mot = Due(30);
        var first = Reminders.ToSend([mot], new HashSet<(int, DueDateKind, DateOnly, int)>());
        Assert.Equal(30, Assert.Single(first).DaysBefore);

        var sent = new HashSet<(int, DueDateKind, DateOnly, int)> { (1, DueDateKind.Mot, mot.Date, 30) };
        Assert.Empty(Reminders.ToSend([mot], sent));
        Assert.Empty(Reminders.ToSend([mot with { Today = Today.AddDays(20) }], sent));   // 10 days left: still the 30-day reminder

        // A week before, the 7-day reminder.
        Assert.Equal(7, Assert.Single(Reminders.ToSend([mot with { Today = Today.AddDays(23) }], sent)).DaysBefore);
    }

    [Fact]
    public void A_date_first_seen_inside_a_week_gets_only_the_7_day_reminder() =>
        Assert.Equal(7, Assert.Single(Reminders.ToSend([Due(3)], new HashSet<(int, DueDateKind, DateOnly, int)>())).DaysBefore);

    [Fact]
    public void A_new_due_date_is_reminded_about_afresh()
    {
        var sent = new HashSet<(int, DueDateKind, DateOnly, int)> { (1, DueDateKind.Mot, Today.AddDays(30), 30) };
        Assert.Single(Reminders.ToSend([Due(29)], sent));
    }

    [Fact]
    public void Reminders_are_kept_apart_by_vehicle_and_kind()
    {
        var sent = new HashSet<(int, DueDateKind, DateOnly, int)> { (1, DueDateKind.Mot, Today.AddDays(20), 30) };
        var toSend = Reminders.ToSend([Due(20), Due(20, DueDateKind.Tax), Due(20, vehicleId: 2), Due(40, DueDateKind.Insurance)], sent);
        Assert.Equal([(1, DueDateKind.Tax), (2, DueDateKind.Mot)], toSend.Select(r => (r.Due.VehicleId, r.Due.Kind)).Order());
    }

    [Fact]
    public void Describes_a_reminder()
    {
        Assert.Equal("MOT for Ford Focus (AB12 CDE) is due on 06/11/2026 (in 30 days).", Reminders.Describe(Due(30)));
        Assert.Equal("Tax for Ford Focus (AB12 CDE) was due on 05/10/2026 (2 days overdue).", Reminders.Describe(Due(-2, DueDateKind.Tax)));
    }

    [Theory]
    [InlineData(-1, "text-bg-danger", true)]
    [InlineData(7, "text-bg-warning", true)]
    [InlineData(30, "text-bg-info", true)]
    [InlineData(31, "text-bg-secondary", false)]
    public void Due_dates_are_coloured_by_how_soon_they_are(int daysLeft, string badge, bool soon)
    {
        var due = Due(daysLeft);
        Assert.Equal((badge, soon), (due.BadgeClass, due.IsSoon));
    }

    [Fact]
    public void A_vehicles_due_dates_are_the_ones_set_soonest_first()
    {
        var vehicle = new VehicleView
        {
            Id = 4, Make = "Ford", Model = "Focus", RegistrationNumber = "AB12 CDE",
            MotDueDate = Today.AddDays(50), InsuranceDueDate = Today.AddDays(5), ServiceDueDate = Today.AddDays(50)
        };
        var dueDates = vehicle.DueDates(Today);
        Assert.Equal([DueDateKind.Insurance, DueDateKind.Mot, DueDateKind.Service], dueDates.Select(d => d.Kind));
        Assert.All(dueDates, d => Assert.Equal((4, "Ford Focus"), (d.VehicleId, d.VehicleName)));
    }
}
