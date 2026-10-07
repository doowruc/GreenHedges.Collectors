using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Services;
using VehicleManager.ViewModels;

namespace VehicleManager.Pages;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    public record VehicleCard(VehicleView Vehicle, FuelSummary AllTime, FuelSummary Year, FuelEntryView? Last, List<DueDateView> DueDates);

    public List<VehicleCard> Cards { get; private set; } = [];

    /// <summary>Due dates within <see cref="Reminders.SoonDays"/> days, or overdue, for every vehicle, soonest first.</summary>
    public List<DueDateView> ComingUp { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        var entries = (await db.FuelEntries
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(FuelEntryView.Projection)
                .ToListAsync())
            .ToLookup(f => f.VehicleId);

        var today = Reminders.UkToday();
        var dueDates = vehicles.ToDictionary(v => v.Id, v => v.DueDates(today, MileageTrend.From(entries[v.Id])));
        ComingUp = dueDates.Values.SelectMany(d => d).Where(d => d.IsSoon)
            .OrderBy(d => d.Date).ThenBy(d => d.VehicleName).ThenBy(d => d.Kind)
            .ToList();

        var yearAgo = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        Cards = vehicles.Select(v => new VehicleCard(
                v,
                stats.Summarise(entries[v.Id]),
                stats.Summarise(entries[v.Id], yearAgo),
                entries[v.Id].FirstOrDefault(),
                dueDates[v.Id]))
            .OrderByDescending(c => c.Last?.Date)
            .ToList();
    }
}
