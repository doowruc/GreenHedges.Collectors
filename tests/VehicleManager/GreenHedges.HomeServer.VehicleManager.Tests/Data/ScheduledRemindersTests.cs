using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resend;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Data;

/// <remarks>
/// Never call <see cref="ScheduledReminders.Invoke"/> here: it covers every user with vehicles, real
/// ones included. Test <see cref="ScheduledReminders.RunForAsync"/> for test users instead.
/// </remarks>
public class ScheduledRemindersTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    /// <summary>A vehicle the fake DVLA/DVSA know (MOT due in 30 days, tax in 60), and a reminder address.</summary>
    private async Task<(string Address, string Registration)> SetUpAsync(string? address = null)
    {
        address ??= FakeResend.NewAddress("remind");
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle
        {
            TaxDueDate = Today.AddDays(60), MotTestExpiryDates = [Today.AddDays(30)]
        });
        await using var db = Db();
        await TestData.AddVehicleAsync(db, reg);
        db.UserSettings.Add(new UserSettings { ReminderEmail = address });
        await db.SaveChangesAsync();
        return (address, reg);
    }

    private async Task<ReminderRunResult> RunAsync(string userId, DateOnly today)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduledReminders>().RunForAsync(userId, today);
    }

    [DatabaseFact]
    public async Task Looks_up_the_dates_then_emails_each_reminder_once()
    {
        var (address, reg) = await SetUpAsync();

        Assert.Equal(new ReminderRunResult(1, 0, ReminderEmailResult.Sent), await RunAsync(UserId, Today));
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal((Today.AddDays(30), Today.AddDays(60)), (vehicle.MotDueDate, vehicle.TaxDueDate));
        }
        var email = Assert.Single(FakeResend.SentTo(address));
        Assert.Equal($"MOT due in 30 days for {reg}", email.Subject);
        Assert.Contains($"MOT for Test Car ({reg}) is due on 06/11/2026 (in 30 days).", email.TextBody);
        Assert.Equal("Vehicle Manager <backups@tests.invalid>", email.From?.ToString());
        Assert.Contains("MOT and tax are checked with the DVLA and DVSA each morning.", email.TextBody);

        // Nothing new the next day: the dates are unchanged and the reminder has been sent.
        Assert.Equal(new ReminderRunResult(0, 0, ReminderEmailResult.NothingDue), await RunAsync(UserId, Today.AddDays(1)));

        // A week before, the MOT's 7-day reminder; and the tax has reached its 30-day one.
        Assert.Equal(ReminderEmailResult.Sent, (await RunAsync(UserId, Today.AddDays(30))).Email);
        email = FakeResend.SentTo(address)[1];
        Assert.Equal("2 vehicle reminders", email.Subject);
        Assert.Contains("MOT for Test Car", email.TextBody);
        Assert.Contains("Tax for Test Car", email.TextBody);

        await using var check = Db();
        Assert.Equal(3, await check.SentReminders.CountAsync());
    }

    [DatabaseFact]
    public async Task Typed_in_dates_are_reminded_about_and_a_new_date_starts_again()
    {
        var (address, _) = await SetUpAsync();
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).InsuranceDueDate = Today.AddDays(5);
            await db.SaveChangesAsync();
        }
        await RunAsync(UserId, Today);
        Assert.Contains("Insurance for Test Car", FakeResend.SentTo(address).Single().TextBody);

        // Renewed: the next year's date gets its own reminders.
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).InsuranceDueDate = Today.AddDays(5).AddYears(1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(ReminderEmailResult.NothingDue, (await RunAsync(UserId, Today.AddDays(1))).Email);
        Assert.Equal(ReminderEmailResult.Sent, (await RunAsync(UserId, Today.AddDays(5).AddYears(1).AddDays(-30))).Email);
        Assert.Contains("Insurance for Test Car", FakeResend.SentTo(address).Last().TextBody);
    }

    [DatabaseFact]
    public async Task Without_an_address_dates_are_still_looked_up()
    {
        await SetUpAsync();
        await using (var db = Db())
        {
            (await db.UserSettings.SingleAsync()).ReminderEmail = null;
            await db.SaveChangesAsync();
        }

        Assert.Equal(new ReminderRunResult(1, 0, ReminderEmailResult.NoAddress), await RunAsync(UserId, Today));
        await using var check = Db();
        Assert.NotNull((await check.Vehicles.SingleAsync()).MotDueDate);
        Assert.Empty(await check.SentReminders.ToListAsync());
    }

    [DatabaseFact]
    public async Task A_lookup_problem_doesnt_stop_the_reminders()
    {
        var (address, _) = await SetUpAsync();
        var unknown = FakeVehicleApis.NewRegistration();
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db, unknown);
            vehicle.ServiceDueDate = Today.AddDays(7);
            await db.SaveChangesAsync();
        }

        var result = await RunAsync(UserId, Today);
        Assert.Equal((1, 2, ReminderEmailResult.Sent), (result.VehiclesUpdated, result.LookupProblems, result.Email));
        Assert.Contains($"Service for Test Car ({unknown})", FakeResend.SentTo(address).Single().TextBody);
    }

    [DatabaseFact]
    public async Task A_failed_email_isnt_recorded_as_sent()
    {
        await SetUpAsync(FakeResend.NewAddress("fail"));
        await Assert.ThrowsAsync<ResendException>(() => RunAsync(UserId, Today));
        await using var check = Db();
        Assert.Empty(await check.SentReminders.ToListAsync());
    }

    [DatabaseFact]
    public async Task Everyone_with_vehicles_is_included()
    {
        await SetUpAsync();
        using var scope = app.Services.CreateScope();
        Assert.Contains(UserId, await scope.ServiceProvider.GetRequiredService<ScheduledReminders>().UsersAsync());
    }

    [DatabaseFact]
    public async Task Sent_reminders_belong_to_the_vehicles_owner()
    {
        await SetUpAsync();
        await RunAsync(UserId, Today);
        await using var other = TestDatabase.As(NewUser());
        Assert.Empty(await other.SentReminders.ToListAsync());
    }
}
