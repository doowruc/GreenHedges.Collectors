using System.IO.Compression;
using System.Net;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Pages;

public class BackupPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task SeedAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, "BACK 1");
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
    }

    private async Task<byte[]> DownloadAsync(PageClient client)
    {
        var page = await client.GetAsync("/Backup");
        Assert.Contains("1 owner(s), 1 vehicle(s), 1 fuel brand(s), 1 station(s), 1 fill-up(s) and 0 service(s)", page.Text);
        var link = PageResponse.Href(System.Text.RegularExpressions.Regex.Match(page.Html, "<a [^>]*>[^<]*<i [^>]*></i>Download backup</a>").Value)!;

        var response = await client.GetResponseAsync(link);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gzip", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"^vehicle-manager-backup-\d{4}-\d{2}-\d{2}-\d{4}\.json\.gz$", response.Content.Headers.ContentDisposition?.FileName);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task AddLaterVehicleAsync()
    {
        await using var db = Db();
        await TestData.AddVehicleAsync(db, "LATER 1");
    }

    private async Task<List<string>> RegistrationsAsync()
    {
        await using var db = Db();
        return await db.Vehicles.OrderBy(v => v.RegistrationNumber).Select(v => v.RegistrationNumber).ToListAsync();
    }

    [DatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Downloading_a_backup_and_restoring_it(bool compressed)
    {
        await SeedAsync();
        var client = app.ClientFor(UserId);
        var file = await DownloadAsync(client);
        if (!compressed)
        {
            using var json = new MemoryStream();
            await new GZipStream(new MemoryStream(file), CompressionMode.Decompress).CopyToAsync(json);
            file = json.ToArray();
        }
        await AddLaterVehicleAsync();

        var form = await client.GetAsync("/Backup/Restore");
        Assert.Contains("Your current 1 owner(s), 2 vehicle(s)", form.Text);
        var restored = await client.PostFileAsync("/Backup/Restore", form, "Upload", compressed ? "backup.json.gz" : "backup.json", file,
            new() { ["Confirm"] = "true" });

        Assert.Equal((HttpStatusCode.Redirect, "/"), (restored.Status, restored.Location));
        Assert.Equal(["BACK 1"], await RegistrationsAsync());
        Assert.Contains("Restored the backup from", (await client.GetAsync("/")).Text);
    }

    [DatabaseFact]
    public async Task Restoring_needs_confirming()
    {
        await SeedAsync();
        var client = app.ClientFor(UserId);
        var file = await DownloadAsync(client);
        await AddLaterVehicleAsync();

        var refused = await client.PostFileAsync("/Backup/Restore", await client.GetAsync("/Backup/Restore"), "Upload", "backup.json.gz", file);
        Assert.Equal(HttpStatusCode.OK, refused.Status);
        Assert.Contains("Tick the box", refused.Errors);
        Assert.Equal(["BACK 1", "LATER 1"], await RegistrationsAsync());
    }

    [DatabaseFact]
    public async Task A_file_that_isnt_a_backup_changes_nothing()
    {
        await SeedAsync();
        var client = app.ClientFor(UserId);

        var refused = await client.PostFileAsync("/Backup/Restore", await client.GetAsync("/Backup/Restore"), "Upload", "backup.json", "{}"u8.ToArray(),
            new() { ["Confirm"] = "true" });
        Assert.Equal(HttpStatusCode.OK, refused.Status);
        Assert.Contains("isn't a valid backup", refused.Errors);
        Assert.Equal(["BACK 1"], await RegistrationsAsync());
    }
}
