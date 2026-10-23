using System.Text.Json;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// Looks up when a vehicle's MOT and tax are due: tax (and MOT, as a fallback) from the DVLA, MOT from
/// the DVSA's MOT history. Each is optional (see <see cref="DvlaOptions"/>, <see cref="MotHistoryOptions"/>),
/// and one failing doesn't stop the other. Refreshing a vehicle also brings its stored MOT tests up to date.
/// </summary>
public class VehicleLookup(AppDbContext db, DvlaClient dvla, DvlaOptions dvlaOptions, MotHistoryClient motHistory, MotHistoryOptions motOptions, ILogger<VehicleLookup> logger)
{
    /// <summary>At least one of the services is set up.</summary>
    public bool IsAvailable => dvlaOptions.IsConfigured || motOptions.IsConfigured;

    /// <summary>The DVLA is set up, so lookups fill in the tax date too.</summary>
    public bool HasTax => dvlaOptions.IsConfigured;

    /// <summary>The DVSA is set up, so lookups bring in each vehicle's MOT history.</summary>
    public bool HasMotHistory => motOptions.IsConfigured;

    /// <summary>The services that are set up, for messages: "the DVLA and DVSA", "the DVLA" or "the DVSA".</summary>
    public string ServiceNames => (dvlaOptions.IsConfigured, motOptions.IsConfigured) switch
    {
        (true, true) => "the DVLA and DVSA",
        (true, false) => "the DVLA",
        (false, true) => "the DVSA",
        _ => ""
    };

    /// <summary>What lookups fill in: "MOT and tax" with the DVLA (which has both), only "MOT" with just the DVSA.</summary>
    public string LookedUp => dvlaOptions.IsConfigured ? "MOT and tax" : "MOT";

    /// <summary>E.g. "MOT and tax are checked with the DVLA and DVSA each morning.", or null if neither is set up.</summary>
    public string? MorningCheck => IsAvailable
        ? $"{LookedUp} {(dvlaOptions.IsConfigured ? "are" : "is")} checked with {ServiceNames} each morning."
        : null;

    /// <summary>A registration as the services take it: no spaces.</summary>
    public static string Compact(string registration) => registration.Replace(" ", "").ToUpperInvariant();

    /// <summary>
    /// Looks up the MOT, the tax, or both. The tax comes from the DVLA; the MOT from the DVSA, with the DVLA's as a
    /// fallback when both are checked (or the DVLA is all there is), so checking just the MOT asks only one service.
    /// </summary>
    public async Task<VehicleLookupResult> LookUpAsync(string registration, LookupParts parts = LookupParts.All)
    {
        var problems = new List<string>();
        var mot = parts.HasFlag(LookupParts.Mot);
        var tax = parts.HasFlag(LookupParts.Tax);
        var askDvla = dvlaOptions.IsConfigured && (tax || !motOptions.IsConfigured);
        var askDvsa = motOptions.IsConfigured && mot;

        DvlaVehicle? fromDvla = null;
        if (askDvla)
        {
            fromDvla = await TryAsync("DVLA", () => dvla.GetAsync(registration), problems);
            if (fromDvla is null && problems.Count == 0) problems.Add($"The DVLA has no vehicle registered {registration}.");
        }

        MotHistoryVehicle? fromDvsa = null;
        if (askDvsa)
        {
            var before = problems.Count;
            fromDvsa = await TryAsync("DVSA", () => motHistory.GetAsync(registration), problems);
            if (fromDvsa is null && problems.Count == before) problems.Add($"The DVSA has no MOT record for {registration}.");
        }

        var sorn = tax && fromDvla?.IsSorn == true;
        var checkedWith = (askDvla, askDvsa) switch
        {
            (true, true) => "the DVLA and DVSA",
            (true, false) => "the DVLA",
            (false, true) => "the DVSA",
            _ => ""
        };
        return new(mot ? fromDvsa?.MotDueDate ?? fromDvla?.MotExpiryDate : null, tax && !sorn ? fromDvla?.TaxDueDate : null, sorn, problems,
            fromDvsa is null ? null : fromDvsa.MotTests ?? [], parts, checkedWith);
    }

    /// <summary>
    /// Looks up a vehicle and copies the dates found onto it, and the DVSA's MOT history to its stored tests
    /// (without saving). Dates that weren't found are left as they are, except that a vehicle declared off the
    /// road has no tax due; without an answer from the DVSA, so are the stored tests. Only <paramref name="parts"/> are checked.
    /// </summary>
    public async Task<VehicleLookupResult> RefreshAsync(Vehicle vehicle, LookupParts parts = LookupParts.All)
    {
        var result = await LookUpAsync(vehicle.RegistrationNumber, parts);
        if (result.MotDueDate is { } mot) vehicle.MotDueDate = mot;
        if (result.TaxDueDate is { } tax) vehicle.TaxDueDate = tax;
        if (result.IsSorn) vehicle.TaxDueDate = null;
        if (result.MotTests is { } tests) await MotTests.SyncAsync(db, vehicle, tests);
        return result;
    }

    private async Task<T?> TryAsync<T>(string service, Func<Task<T?>> lookUp, List<string> problems) where T : class
    {
        try
        {
            return await lookUp();
        }
        catch (VehicleLookupException ex)
        {
            logger.LogWarning(ex, "{Service} lookup failed", service);   // not the registration: logs may leave the app (Telemetry)
            problems.Add(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "{Service} lookup failed", service);
            problems.Add($"The {service} couldn't be reached or sent something unexpected.");
        }
        return null;
    }
}
