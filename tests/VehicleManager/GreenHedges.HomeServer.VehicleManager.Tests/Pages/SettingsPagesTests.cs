using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class SettingsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Settings_is_in_the_user_menu()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");
        var menu = Regex.Match(page.Html, @"aria-label=""Account"".*?<ul[^>]*>(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"<a class=""dropdown-item"" href=""/Settings"">.*Settings</a>", menu);
    }

    [DatabaseFact]
    public async Task Saving_and_clearing_the_backup_email()
    {
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync("/Settings");
        Assert.Contains("Last scheduled backup: never", form.Text);
        Assert.Matches(@"<button[^>]*disabled[^>]*>\s*<i[^>]*></i>Email a backup now", form.Html);

        var invalid = await client.PostFormAsync("/Settings", form, new() { ["Settings.BackupEmail"] = "not an address" });
        Assert.Equal(HttpStatusCode.OK, invalid.Status);
        Assert.Contains("not a valid e-mail address", invalid.Errors);

        var saved = await client.PostFormAsync("/Settings", form, new() { ["Settings.BackupEmail"] = " me@tests.invalid " });
        Assert.Equal((HttpStatusCode.Redirect, "/Settings"), (saved.Status, saved.Location));
        form = await client.GetAsync("/Settings");
        Assert.Contains("value=\"me@tests.invalid\"", form.Html);

        // Saved again from the reloaded form, so its row version is current.
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Settings", form, new() { ["Settings.BackupEmail"] = "" })).Status);
        Assert.DoesNotContain("me@tests.invalid", (await client.GetAsync("/Settings")).Html);
    }

    [DatabaseFact]
    public async Task Settings_belong_to_each_user()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Settings", await client.GetAsync("/Settings"), new() { ["Settings.BackupEmail"] = "mine@tests.invalid" });
        Assert.DoesNotContain("mine@tests.invalid", (await app.ClientFor(NewUser()).GetAsync("/Settings")).Html);
    }

    [DatabaseFact]
    public async Task Emailing_a_backup_now()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        var address = FakeResend.NewAddress();
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Settings", await client.GetAsync("/Settings"), new() { ["Settings.BackupEmail"] = address });

        var page = await client.GetAsync("/Settings");
        var sent = await client.PostFormAsync("/Settings?handler=Send", page);
        Assert.Equal((HttpStatusCode.Redirect, "/Settings"), (sent.Status, sent.Location));
        Assert.Single(FakeResend.SentTo(address));

        page = await client.GetAsync("/Settings");
        Assert.Contains($"Backup emailed to {address}.", page.Text);
        Assert.Contains("Last scheduled backup: never", page.Text);   // a requested backup doesn't affect the schedule

        // Sent even though nothing has changed.
        await client.PostFormAsync("/Settings?handler=Send", page);
        Assert.Equal(2, FakeResend.SentTo(address).Count);
    }

    [DatabaseFact]
    public async Task A_failed_email_is_reported()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Settings", await client.GetAsync("/Settings"), new() { ["Settings.BackupEmail"] = FakeResend.NewAddress("fail") });

        var failed = await client.PostFormAsync("/Settings?handler=Send", await client.GetAsync("/Settings"));
        Assert.Equal(HttpStatusCode.OK, failed.Status);
        Assert.Contains("couldn't be emailed: Test failure", failed.Text);
        Assert.Contains("Last scheduled backup: never", failed.Text);
    }
}
