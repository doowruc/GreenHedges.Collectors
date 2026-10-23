using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>
/// One brand/station pair on the fill-up form, typed as names. Both are needed (see
/// <c>FuelStops.ValidateAsync</c>); a row with neither is ignored.
/// </summary>
public class FuelStopInput
{
    [StringLength(FuelBrand.MaxNameLength)]
    [Display(Name = "Fuel brand")]
    public string? Brand { get; set; }

    [StringLength(FuelStation.MaxNameLength)]
    [Display(Name = "Station")]
    public string? Station { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Brand) && string.IsNullOrWhiteSpace(Station);

    /// <summary>
    /// Pairs up spreadsheet-style text, where a split fill-up is written "Bx + By" / "Lx + Ly"
    /// (brands and locations in the same order). Returns no stops if both are blank.
    /// </summary>
    public static List<FuelStopInput> FromText(string? brands, string? stations)
    {
        static string?[] Parts(string? s) =>
            string.IsNullOrWhiteSpace(s) ? [] : s.Split('+').Select(p => p.Trim()).Select(p => p == "" ? null : p).ToArray();

        var b = Parts(brands);
        var l = Parts(stations);
        return Enumerable.Range(0, Math.Max(b.Length, l.Length))
            .Select(i => new FuelStopInput { Brand = i < b.Length ? b[i] : null, Station = i < l.Length ? l[i] : null })
            .Where(s => !s.IsEmpty)
            .ToList();
    }
}
