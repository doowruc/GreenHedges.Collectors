using Microsoft.EntityFrameworkCore;
using Resend;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>The address backup and reminder emails come from (<c>Resend:From</c>); its domain must be verified in Resend.</summary>
public sealed record EmailOptions(string From);

public enum BackupEmailResult
{
    /// <summary>The user hasn't set a backup email address.</summary>
    NoAddress,
    /// <summary>Nothing has changed since the last scheduled backup, so none was sent.</summary>
    Unchanged,
    Sent
}

/// <summary>Emails the signed-in user a backup of their data, through Resend, to their backup email address.</summary>
public class BackupEmailer(AppDbContext db, BackupService backups, IResend resend, EmailOptions options)
{
    private static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>
    /// Emails a backup to the user's backup address. A <paramref name="scheduled"/> backup is only
    /// sent if the data has changed since the last scheduled one, and becomes the last scheduled one.
    /// Otherwise (on request) it's always sent, and doesn't affect the schedule.
    /// </summary>
    /// <exception cref="ResendException">Resend didn't accept the email.</exception>
    public async Task<BackupEmailResult> EmailAsync(bool scheduled)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync();
        if (settings?.BackupEmail is not { Length: > 0 } address) return BackupEmailResult.NoAddress;

        var backup = await backups.CreateAsync();
        var hash = backup.ContentHash();
        if (scheduled && hash == settings.LastBackupHash) return BackupEmailResult.Unchanged;

        var made = TimeZoneInfo.ConvertTimeFromUtc(backup.CreatedDate, UkTime);
        await resend.EmailSendAsync(new EmailMessage
        {
            From = options.From,
            To = address,
            Subject = $"Vehicle Manager backup {made:dd/MM/yyyy}",
            TextBody = $"""
                Attached is a backup of your Vehicle Manager data, made at {made:HH:mm} on {made:dd/MM/yyyy}: {backup.Counts}.

                To restore it, go to Manage › Restore in Vehicle Manager and choose the attached file.
                """,
            Attachments = [new EmailAttachment { Filename = backup.FileName, Content = await backup.ToBytesAsync(), ContentType = "application/gzip" }]
        });

        if (scheduled)
        {
            settings.LastBackupDate = backup.CreatedDate;
            settings.LastBackupHash = hash;
            await db.SaveChangesAsync();
        }
        return BackupEmailResult.Sent;
    }
}
