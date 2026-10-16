using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Tests.Unit;

/// <summary>How the DVSA's tests are copied to a vehicle. With a vehicle that isn't saved, nothing touches the database.</summary>
public class MotTestsTests
{
    private static readonly DateTimeOffset Completed = new(2025, 11, 3, 10, 30, 0, TimeSpan.Zero);

    private static MotHistoryTest Test(DateTimeOffset? completed = null, string result = "PASSED", string? odometer = "45123",
        string? unit = "MI", string? odometerType = "READ", string? dataSource = "DVSA", params MotHistoryDefect[] defects) =>
        new(completed ?? Completed, result, new DateOnly(2026, 11, 2), odometer, unit, odometerType, "123456789012", "AB12 CDE", dataSource, [.. defects]);

    private static async Task<Vehicle> SyncAsync(params MotHistoryTest[] tests)
    {
        var vehicle = new Vehicle();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused.invalid").Options);
        await MotTests.SyncAsync(db, vehicle, tests);
        return vehicle;
    }

    [Fact]
    public async Task Copies_a_tests_details_and_defects_in_order()
    {
        var vehicle = await SyncAsync(Test(defects:
        [
            new("Tyre worn close to legal limit", "ADVISORY", false),
            new("  ", "ADVISORY", false),                       // no text, so left out
            new("Brake pipe corroded", "MAJOR", true)
        ]));

        var test = Assert.Single(vehicle.MotTests);
        Assert.Equal((Completed.UtcDateTime, true, new DateOnly(2026, 11, 2), 45123, false, "123456789012", "AB12 CDE", "DVSA"),
            (test.CompletedDate, test.Passed, test.ExpiryDate, test.Odometer, test.OdometerInKilometres, test.MotTestNumber, test.RegistrationAtTest, test.DataSource));
        Assert.Equal([(1, "ADVISORY", "Tyre worn close to legal limit", false), (2, "MAJOR", "Brake pipe corroded", true)],
            test.Defects.Select(d => (d.Sequence, d.Type, d.Text, d.Dangerous)));
    }

    [Fact]
    public async Task Reads_failures_kilometres_and_unread_odometers()
    {
        var vehicle = await SyncAsync(
            Test(Completed, result: "FAILED", odometer: "72000", unit: "KM"),
            Test(Completed.AddDays(1), odometer: "0", unit: null, odometerType: "UNREADABLE"),
            Test(Completed.AddDays(2), odometer: "lots"));
        Assert.Equal([(false, 72000, true), (true, null, false), (true, null, false)],
            vehicle.MotTests.Select(t => (t.Passed, t.Odometer, t.OdometerInKilometres)));
    }

    [Fact]
    public async Task Skips_a_test_without_a_completion_date_and_cuts_overlong_text()
    {
        var vehicle = await SyncAsync(
            Test() with { CompletedDate = null },
            Test(Completed.AddDays(1), defects: new MotHistoryDefect(new string('x', 3000), "USER ENTERED", null)));
        var defect = Assert.Single(Assert.Single(vehicle.MotTests).Defects);
        Assert.Equal(MotDefect.MaxTextLength, defect.Text.Length);
    }

    [Theory]
    [InlineData("ADVISORY", false, "Advisory", "text-bg-secondary", false)]
    [InlineData("MINOR", false, "Minor", "text-bg-warning", false)]
    [InlineData("MAJOR", true, "Major", "text-bg-danger", true)]
    [InlineData("DANGEROUS", true, "Dangerous", "text-bg-danger", false)]   // its type already says so
    [InlineData("PRS", false, "Fixed at test", "text-bg-warning", false)]
    [InlineData(null, false, "Note", "text-bg-secondary", false)]
    public void Defects_are_labelled_by_type(string? type, bool dangerous, string label, string badge, bool showDangerous)
    {
        var defect = new MotDefectView { Type = type, Text = "x", Dangerous = dangerous };
        Assert.Equal((label, badge, showDangerous), (defect.Label, defect.BadgeClass, defect.ShowDangerous));
    }

    [Fact]
    public void A_tests_date_is_the_UK_date()
    {
        // 23:30 UTC in British Summer Time is half past midnight the next day.
        var test = new MotTestView { CompletedDate = new DateTime(2025, 7, 1, 23, 30, 0) };
        Assert.Equal(new DateOnly(2025, 7, 2), test.Date);
    }

    [Fact]
    public void Northern_Irish_tests_have_no_defect_details()
    {
        Assert.False(new MotTestView { DataSource = "DVA NI" }.HasDefectDetails);
        Assert.True(new MotTestView { DataSource = "DVSA" }.HasDefectDetails);
        Assert.Equal("72,000 km", new MotTestView { Odometer = 72000, OdometerInKilometres = true }.OdometerText);
    }
}
