using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>
/// How fast a vehicle is adding miles, from its odometer readings (its fill-ups' and its MOT tests'): the latest
/// reading, and the average miles per day since the earliest reading in the last <see cref="RecentMonths"/> months
/// (or, if those span less than <see cref="MinDays"/> days, since the earliest reading of all).
/// </summary>
public sealed record MileageTrend(DateOnly LatestDate, int LatestOdometer, double MilesPerDay)
{
    public const int RecentMonths = 6;
    public const int MinDays = 28;

    public double MilesPerMonth => MilesPerDay * 365.25 / 12;

    /// <summary>When the odometer will reach (or reached) <paramref name="mileage"/>, at this rate.</summary>
    public DateOnly DateAt(int mileage) =>
        LatestDate.AddDays((int)Math.Clamp(Math.Round((mileage - LatestOdometer) / MilesPerDay), -36500, 36500));

    /// <returns>Null without two readings at least <see cref="MinDays"/> days apart, or if the miles didn't go up.</returns>
    public static MileageTrend? From(IEnumerable<(DateOnly Date, int Odometer)> readings)
    {
        var sorted = readings.OrderBy(r => r.Date).ThenBy(r => r.Odometer).ToList();
        if (sorted.Count < 2) return null;
        var latest = sorted[^1];
        var since = latest.Date.AddMonths(-RecentMonths);
        var earliest = sorted.First(r => r.Date >= since);
        if (latest.Date.DayNumber - earliest.Date.DayNumber < MinDays) earliest = sorted[0];

        var days = latest.Date.DayNumber - earliest.Date.DayNumber;
        var miles = latest.Odometer - earliest.Odometer;
        return days >= MinDays && miles > 0 ? new(latest.Date, latest.Odometer, (double)miles / days) : null;
    }

    /// <summary>The trend for each of the current user's vehicles that has one.</summary>
    public static async Task<Dictionary<int, MileageTrend>> ForVehiclesAsync(AppDbContext db)
    {
        var fillUps = (await db.FuelEntries.Where(f => f.Odometer != null)
                .Select(f => new { f.VehicleId, f.Date, Odometer = f.Odometer!.Value })
                .ToListAsync())
            .ToLookup(f => f.VehicleId, f => (f.Date, f.Odometer));
        var motTests = await MotReadingsAsync(db);
        return fillUps.Select(g => g.Key).Union(motTests.Select(g => g.Key))
            .Select(id => (Id: id, Trend: From(fillUps[id].Concat(motTests[id]))))
            .Where(t => t.Trend is not null)
            .ToDictionary(t => t.Id, t => t.Trend!);
    }

    /// <summary>The trend from fill-ups and MOT readings (<see cref="MotReadingsAsync"/>) already loaded.</summary>
    public static MileageTrend? From(IEnumerable<ViewModels.FuelEntryView> entries, IEnumerable<(DateOnly Date, int Odometer)> motReadings) =>
        From(entries.Where(e => e.Odometer is not null).Select(e => (e.Date, e.Odometer!.Value)).Concat(motReadings));

    /// <summary>The odometer readings of the current user's MOT tests, in miles, by vehicle id.</summary>
    public static async Task<ILookup<int, (DateOnly Date, int Odometer)>> MotReadingsAsync(AppDbContext db) =>
        (await db.MotTests.Where(t => t.Odometer != null)
            .Select(t => new { t.VehicleId, t.CompletedDate, Odometer = t.Odometer!.Value, t.OdometerInKilometres })
            .ToListAsync())
        .ToLookup(t => t.VehicleId, t => (Reminders.UkDate(t.CompletedDate), Miles(t.Odometer, t.OdometerInKilometres)));

    /// <summary>An odometer reading in miles: one in kilometres is converted.</summary>
    public static int Miles(int odometer, bool inKilometres) =>
        inKilometres ? (int)Math.Round(odometer / 1.609344) : odometer;
}
