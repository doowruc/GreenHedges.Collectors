using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.ViewModels;

namespace VehicleManager.Services;

/// <summary>
/// Turns typed brand and station names into a fill-up's stops. Reuses the user's brands, and
/// stations within a brand (matching names ignoring case), and creates any new ones; they're added
/// to the context and saved with the fill-up.
/// </summary>
public class FuelStops(AppDbContext db)
{
    private Dictionary<string, FuelBrand>? _brands;
    private List<FuelStation>? _stations;

    public static string NormaliseName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Tidies the typed names and checks every row (other than blank ones) has a brand and a
    /// station. A station typed without a brand gets its brand filled in when exactly one brand has
    /// a station with that name. Errors go in <paramref name="state"/> under
    /// <c>{prefix}[i].Brand</c> / <c>.Station</c>.
    /// </summary>
    public async Task ValidateAsync(IList<FuelStopInput> inputs, ModelStateDictionary state, string prefix)
    {
        await LoadAsync();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            input.Brand = NormaliseName(input.Brand);
            input.Station = NormaliseName(input.Station);
            if (input.IsEmpty) continue;

            if (input.Station.Length == 0)
                state.AddModelError($"{prefix}[{i}].Station", "Enter the station.");
            else if (input.Brand.Length == 0)
            {
                var brands = _stations!.Where(s => SameName(s.Name, input.Station)).Select(s => s.FuelBrand!).Distinct().ToList();
                if (brands.Count == 1)
                {
                    input.Brand = brands[0].Name;
                    state.Remove($"{prefix}[{i}].Brand");   // so the form shows the filled-in brand
                }
                else
                {
                    state.AddModelError($"{prefix}[{i}].Brand", brands.Count == 0
                        ? "Enter the brand of this new station."
                        : $"More than one brand has a station called {input.Station}: enter the brand.");
                }
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="entry"/>'s stops match <paramref name="inputs"/> (blank rows are ignored;
    /// the rest must have a brand and station, see <see cref="ValidateAsync"/>). Existing stops are
    /// updated in place, so their audit history is kept. For an existing fill-up, load its stops first.
    /// </summary>
    public async Task SetStopsAsync(FuelEntry entry, IEnumerable<FuelStopInput> inputs)
    {
        await LoadAsync();
        var wanted = inputs.Where(i => !i.IsEmpty).ToList();
        var stops = entry.Stops.OrderBy(s => s.Sequence).ToList();
        var changed = false;

        for (var i = 0; i < wanted.Count; i++)
        {
            var station = FindOrCreateStation(wanted[i]);
            if (i < stops.Count)
            {
                if (stops[i].FuelStation != station)
                {
                    stops[i].FuelStation = station;
                    changed = true;
                }
            }
            else
            {
                entry.Stops.Add(new FuelEntryStop { Sequence = i + 1, FuelStation = station });
                changed = true;
            }
        }
        foreach (var extra in stops.Skip(wanted.Count))
        {
            entry.Stops.Remove(extra);
            db.FuelEntryStops.Remove(extra);
            changed = true;
        }

        // Stops are separate rows, so changing only them wouldn't touch the fill-up itself. Mark it
        // modified so its audit columns and RowVersion change, and conflicting edits are detected.
        if (changed && db.Entry(entry).State == EntityState.Unchanged)
            db.Entry(entry).Property(e => e.ModifiedDate).IsModified = true;
    }

    private async Task LoadAsync()
    {
        // Tracked, so EF links each station to its brand (FuelStation.FuelBrand).
        _brands ??= await db.FuelBrands.ToDictionaryAsync(b => b.Name, StringComparer.OrdinalIgnoreCase);
        _stations ??= await db.FuelStations.ToListAsync();
    }

    private FuelStation FindOrCreateStation(FuelStopInput input)
    {
        var brandName = NormaliseName(input.Brand);
        var stationName = NormaliseName(input.Station);
        if (brandName.Length == 0 || stationName.Length == 0)
            throw new InvalidOperationException("A fill-up stop needs a brand and a station.");

        if (!_brands!.TryGetValue(brandName, out var brand))
            _brands[brandName] = brand = db.FuelBrands.Add(new FuelBrand { Name = brandName }).Entity;

        var station = _stations!.FirstOrDefault(s => s.FuelBrand == brand && SameName(s.Name, stationName));
        if (station is null)
        {
            station = db.FuelStations.Add(new FuelStation { FuelBrand = brand, Name = stationName }).Entity;
            _stations!.Add(station);
        }
        return station;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
