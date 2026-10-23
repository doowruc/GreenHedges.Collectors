using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class ServiceScheduleTests
{
    private static readonly DateOnly Serviced = new(2026, 10, 8);

    [Fact]
    public void The_next_service_is_an_interval_after_this_one() =>
        Assert.Equal((new DateOnly(2027, 10, 8), 55000), ServiceSchedule.NextAfter(Serviced, 45000, 12, 10000));

    [Fact]
    public void Only_the_intervals_set_are_worked_out()
    {
        Assert.Equal((new DateOnly(2027, 4, 8), (int?)null), ServiceSchedule.NextAfter(Serviced, 45000, 6, null));
        Assert.Equal(((DateOnly?)null, 57500), ServiceSchedule.NextAfter(Serviced, 45000, null, 12500));
        Assert.Equal(((DateOnly?)null, (int?)null), ServiceSchedule.NextAfter(Serviced, 45000, null, null));
    }

    [Fact]
    public void Months_at_the_end_of_a_month_land_on_its_last_day() =>
        Assert.Equal(new DateOnly(2027, 2, 28), ServiceSchedule.NextAfter(new DateOnly(2026, 8, 31), 0, 6, null).Date);

    [Theory]
    [InlineData("2027-10-01", "2027-10-13", "12 days late")]
    [InlineData("2027-10-01", "2027-10-02", "1 day late")]
    [InlineData("2027-10-01", "2027-11-01", "1 month late")]
    [InlineData("2027-10-31", "2027-12-30", "1 month late")]
    [InlineData("2027-10-01", "2028-01-15", "3 months late")]
    public void Says_how_late_a_service_was(string due, string done, string expected) =>
        Assert.Equal(expected, ServiceSchedule.LateBy(DateOnly.Parse(due), DateOnly.Parse(done)));

    [Fact]
    public void Each_service_is_due_an_interval_after_the_one_before()
    {
        ServiceRecordView Service(int id, string date, int mileage) => new() { Id = id, Date = DateOnly.Parse(date), Mileage = mileage };
        // Out of order, as the page lists them newest first.
        List<ServiceRecordView> services = [Service(3, "2026-12-01", 56000), Service(1, "2024-10-01", 30000), Service(2, "2025-09-01", 41000)];

        ServiceSchedule.SetDue(services, 12, 10000);
        var byId = services.ToDictionary(s => s.Id);
        Assert.Equal(((DateOnly?)null, (int?)null, (string?)null, (string?)null),
            (byId[1].DueDate, byId[1].DueMileage, byId[1].LateText, byId[1].MilesOverText));                  // the first: nothing before it
        Assert.Equal((new DateOnly(2025, 10, 1), 40000, (string?)null, "1,000 miles over"),
            (byId[2].DueDate!.Value, byId[2].DueMileage!.Value, byId[2].LateText, byId[2].MilesOverText));     // early by date, over by miles
        Assert.Equal((new DateOnly(2026, 9, 1), 51000, "3 months late", "5,000 miles over"),
            (byId[3].DueDate!.Value, byId[3].DueMileage!.Value, byId[3].LateText, byId[3].MilesOverText));
    }

    [Fact]
    public void Without_an_interval_nothing_is_late()
    {
        List<ServiceRecordView> services = [new() { Id = 1, Date = new(2020, 1, 1), Mileage = 1000 }, new() { Id = 2, Date = new(2026, 1, 1), Mileage = 90000 }];
        ServiceSchedule.SetDue(services, null, 10000);
        Assert.Null(services[1].LateText);
        Assert.Equal("79,000 miles over", services[1].MilesOverText);
    }

    [Fact]
    public void On_the_due_date_or_mileage_isnt_late()
    {
        List<ServiceRecordView> services = [new() { Id = 1, Date = new(2025, 1, 1), Mileage = 1000 }, new() { Id = 2, Date = new(2026, 1, 1), Mileage = 11000 }];
        ServiceSchedule.SetDue(services, 12, 10000);
        Assert.Equal(((string?)null, (string?)null), (services[1].LateText, services[1].MilesOverText));
    }
}
