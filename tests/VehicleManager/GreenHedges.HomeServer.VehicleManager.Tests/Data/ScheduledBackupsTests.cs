using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resend;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Data;

/// <remarks>
/// Never call <see cref="ScheduledBackups.Invoke"/> here: it backs up every user with a backup
/// address, real ones included. Test <see cref="ScheduledBackups.BackUpAsync"/> for test users instead.
/// </remarks>
public class ScheduledBackupsTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task<string> SetUpAsync()
    {
        var address = FakeResend.NewAddress();
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, "SCHED 1");
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
        db.UserSettings.Add(new UserSettings { BackupEmail = address });
        await db.SaveChangesAsync();
        return address;
    }

    private async Task<BackupEmailResult> BackUpAsync(string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduledBackups>().BackUpAsync(userId);
    }

    [DatabaseFact]
    public async Task Emails_a_backup_only_when_the_data_has_changed_since_the_last_one()
    {
        var address = await SetUpAsync();

        Assert.Equal(BackupEmailResult.Sent, await BackUpAsync(UserId));
        var email = Assert.Single(FakeResend.SentTo(address));
        Assert.Equal("Vehicle Manager <backups@tests.invalid>", email.From?.ToString());
        var attachment = Assert.Single(email.Attachments!);
        Assert.Matches(@"^vehicle-manager-backup-.*\.json\.gz$", attachment.Filename);
        Assert.Equal("application/gzip", attachment.ContentType);
        var backup = BackupFile.Read(new MemoryStream(attachment.Content!.Value.ByteArray!));
        Assert.Equal(new BackupCounts(1, 1, 1, 1, 1, 0), backup.Counts);
        Assert.Contains("1 fill-up(s)", email.TextBody);

        Assert.Equal(BackupEmailResult.Unchanged, await BackUpAsync(UserId));

        await using (var db = Db())
            await TestData.AddFillUpAsync(db, (await db.Vehicles.SingleAsync()).Id, new DateOnly(2026, 2, 1), ("Shell", "Meadowbank"));
        Assert.Equal(BackupEmailResult.Sent, await BackUpAsync(UserId));

        // A deletion leaves no modified date behind, but still counts as a change.
        await using (var db = Db())
        {
            await db.FuelEntryStops.Where(s => s.FuelEntry!.Date == new DateOnly(2026, 2, 1)).ExecuteDeleteAsync();
            await db.FuelEntries.Where(f => f.Date == new DateOnly(2026, 2, 1)).ExecuteDeleteAsync();
        }
        Assert.Equal(BackupEmailResult.Sent, await BackUpAsync(UserId));
        Assert.Equal(3, FakeResend.SentTo(address).Count);

        await using var check = Db();
        Assert.NotNull((await check.UserSettings.SingleAsync()).LastBackupDate);
    }

    [DatabaseFact]
    public async Task Only_scheduled_backups_count_as_the_last_backup()
    {
        var address = await SetUpAsync();
        var client = app.ClientFor(UserId);
        Assert.True((await client.GetResponseAsync("/Backup?handler=Download")).IsSuccessStatusCode);
        await client.PostFormAsync("/Settings?handler=Send", await client.GetAsync("/Settings"));
        Assert.Single(FakeResend.SentTo(address));

        // Neither the download nor the requested email counts, so the schedule still sends one.
        Assert.Equal(BackupEmailResult.Sent, await BackUpAsync(UserId));
        Assert.Equal(BackupEmailResult.Unchanged, await BackUpAsync(UserId));

        // And a requested email after it doesn't reset it.
        await client.PostFormAsync("/Settings?handler=Send", await client.GetAsync("/Settings"));
        Assert.Equal(BackupEmailResult.Unchanged, await BackUpAsync(UserId));
        Assert.Equal(3, FakeResend.SentTo(address).Count);
    }

    [DatabaseFact]
    public async Task Only_users_with_a_backup_address_are_backed_up()
    {
        await SetUpAsync();
        var noAddress = NewUser();
        await using (var db = TestDatabase.As(noAddress))
        {
            await TestData.AddVehicleAsync(db);
            db.UserSettings.Add(new UserSettings());
            await db.SaveChangesAsync();
        }

        using var scope = app.Services.CreateScope();
        var users = await scope.ServiceProvider.GetRequiredService<ScheduledBackups>().UsersToBackUpAsync();
        Assert.Contains(UserId, users);
        Assert.DoesNotContain(noAddress, users);
        Assert.Equal(BackupEmailResult.NoAddress, await BackUpAsync(noAddress));
    }

    [DatabaseFact]
    public async Task A_failed_email_isnt_recorded_as_a_backup()
    {
        await SetUpAsync();
        await using (var db = Db())
        {
            (await db.UserSettings.SingleAsync()).BackupEmail = FakeResend.NewAddress("fail");
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ResendException>(() => BackUpAsync(UserId));
        await using var check = Db();
        Assert.Null((await check.UserSettings.SingleAsync()).LastBackupDate);
    }

    [DatabaseFact]
    public void The_scheduler_doesnt_run_in_tests() =>
        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), s => s.GetType().Namespace!.StartsWith("Coravel"));
}
