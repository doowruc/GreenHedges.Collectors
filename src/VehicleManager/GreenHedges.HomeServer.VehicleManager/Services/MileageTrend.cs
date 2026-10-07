using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;

namespace VehicleManager.Services;

/// <summary>
/// How fast a vehicle is adding miles, from its fill-ups' odometer readings: the latest reading, and the
/// average miles per day since the earliest reading in the last <see cref="RecentMonths"/> months (or,
/// if those span less than <see cref="MinDays"/> days, since the earliest reading of all).
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
    public static async Task<Dictionary<int, MileageTrend>> ForVehiclesAsync(AppDbContext db) =>
        (await db.FuelEntries.Where(f => f.Odometer != null)
            .Select(f => new { f.VehicleId, f.Date, Odometer = f.Odometer!.Value })
            .ToListAsync())
        .GroupBy(f => f.VehicleId)
        .Select(g => (g.Key, Trend: From(g.Select(f => (f.Date, f.Odometer)))))
        .Where(t => t.Trend is not null)
        .ToDictionary(t => t.Key, t => t.Trend!);

    /// <summary>The trend from fill-ups already loaded.</summary>
    public static MileageTrend? From(IEnumerable<ViewModels.FuelEntryView> entries) =>
        From(entries.Where(e => e.Odometer is not null).Select(e => (e.Date, e.Odometer!.Value)));
}
