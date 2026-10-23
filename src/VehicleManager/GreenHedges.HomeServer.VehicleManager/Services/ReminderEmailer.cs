using Microsoft.EntityFrameworkCore;
using Resend;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Services;

/// <summary>Emails the signed-in user any reminders of their vehicles' due dates that haven't been sent yet (see <see cref="Reminders"/>).</summary>
public class ReminderEmailer(AppDbContext db, IResend resend, EmailOptions options, VehicleLookup lookup)
{
    /// <exception cref="ResendException">Resend didn't accept the email; nothing is recorded as sent, so it's tried again next time.</exception>
    public async Task<ReminderEmailResult> EmailAsync(DateOnly today)
    {
        var address = await db.UserSettings.Select(s => s.ReminderEmail).FirstOrDefaultAsync();
        if (address is not { Length: > 0 }) return ReminderEmailResult.NoAddress;

        var mileage = await MileageTrend.ForVehiclesAsync(db);
        var dueDates = (await db.Vehicles.Select(VehicleView.Projection).ToListAsync())
            .SelectMany(v => v.DueDates(today, mileage.GetValueOrDefault(v.Id)));
        var sent = (await db.SentReminders.Select(r => new { r.VehicleId, r.Kind, r.DueDate, r.DueMileage, r.DaysBefore }).ToListAsync())
            .Select(r => ReminderKey.For(r.VehicleId, r.Kind, r.DueDate, r.DueMileage, r.DaysBefore))
            .ToHashSet();
        var reminders = Reminders.ToSend(dueDates, sent);
        if (reminders.Count == 0) return ReminderEmailResult.NothingDue;

        var first = reminders[0].Due;
        await resend.EmailSendAsync(new EmailMessage
        {
            From = options.From,
            To = address,
            Subject = reminders.Count == 1
                ? $"{first.Name} {(first.DaysLeft < 0 ? "overdue" : "due " + first.When)} for {first.RegistrationNumber}"
                : $"{reminders.Count} vehicle reminders",
            TextBody = string.Join("\n", reminders.Select(r => "- " + Reminders.Describe(r.Due))) + "\n\n" +
                       "Once it's done, update the date in Vehicle Manager." + (lookup.MorningCheck is { } check ? " " + check : "") + "\n"
        });

        db.SentReminders.AddRange(reminders.Select(r => new SentReminder
        {
            VehicleId = r.Due.VehicleId, Kind = r.Due.Kind, DueDate = r.Due.Date, DueMileage = r.Due.DueMileage, DaysBefore = r.DaysBefore
        }));
        await db.SaveChangesAsync();
        return ReminderEmailResult.Sent;
    }
}
