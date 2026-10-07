using System.Text.Json;
using VehicleManager.Models;

namespace VehicleManager.Services;

/// <summary>What a lookup found. Dates it didn't find are null; <see cref="Problems"/> says why, for showing to the user.</summary>
public sealed record VehicleLookupResult(DateOnly? MotDueDate, DateOnly? TaxDueDate, bool IsSorn, IReadOnlyList<string> Problems)
{
    /// <summary>E.g. "MOT due 06/11/2026, tax due 01/03/2027".</summary>
    public string Summary
    {
        get
        {
            var found = new List<string>();
            if (MotDueDate is { } mot) found.Add($"MOT due {mot:dd/MM/yyyy}");
            if (TaxDueDate is { } tax) found.Add($"tax due {tax:dd/MM/yyyy}");
            if (IsSorn) found.Add("declared off the road (SORN), so no tax is due");
            return found.Count == 0 ? "no MOT or tax dates found" : string.Join(", ", found);
        }
    }
}

/// <summary>
/// Looks up when a vehicle's MOT and tax are due: tax (and MOT, as a fallback) from the DVLA, MOT from
/// the DVSA's MOT history. Each is optional (see <see cref="DvlaOptions"/>, <see cref="MotHistoryOptions"/>),
/// and one failing doesn't stop the other.
/// </summary>
public class VehicleLookup(DvlaClient dvla, DvlaOptions dvlaOptions, MotHistoryClient motHistory, MotHistoryOptions motOptions, ILogger<VehicleLookup> logger)
{
    /// <summary>At least one of the services is set up.</summary>
    public bool IsAvailable => dvlaOptions.IsConfigured || motOptions.IsConfigured;

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

    public async Task<VehicleLookupResult> LookUpAsync(string registration)
    {
        var problems = new List<string>();

        DvlaVehicle? fromDvla = null;
        if (dvlaOptions.IsConfigured)
        {
            fromDvla = await TryAsync("DVLA", registration, () => dvla.GetAsync(registration), problems);
            if (fromDvla is null && problems.Count == 0) problems.Add($"The DVLA has no vehicle registered {registration}.");
        }

        MotHistoryVehicle? fromDvsa = null;
        if (motOptions.IsConfigured)
        {
            var before = problems.Count;
            fromDvsa = await TryAsync("DVSA", registration, () => motHistory.GetAsync(registration), problems);
            if (fromDvsa is null && problems.Count == before) problems.Add($"The DVSA has no MOT record for {registration}.");
        }

        var sorn = fromDvla?.IsSorn == true;
        return new(fromDvsa?.MotDueDate ?? fromDvla?.MotExpiryDate, sorn ? null : fromDvla?.TaxDueDate, sorn, problems);
    }

    /// <summary>
    /// Looks up a vehicle and copies the dates found onto it (without saving). Dates that weren't found
    /// are left as they are, except that a vehicle declared off the road has no tax due.
    /// </summary>
    public async Task<VehicleLookupResult> RefreshAsync(Vehicle vehicle)
    {
        var result = await LookUpAsync(vehicle.RegistrationNumber);
        if (result.MotDueDate is { } mot) vehicle.MotDueDate = mot;
        if (result.TaxDueDate is { } tax) vehicle.TaxDueDate = tax;
        if (result.IsSorn) vehicle.TaxDueDate = null;
        return result;
    }

    private async Task<T?> TryAsync<T>(string service, string registration, Func<Task<T?>> lookUp, List<string> problems) where T : class
    {
        try
        {
            return await lookUp();
        }
        catch (VehicleLookupException ex)
        {
            logger.LogWarning(ex, "{Service} lookup of {Registration} failed", service, registration);
            problems.Add(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "{Service} lookup of {Registration} failed", service, registration);
            problems.Add($"The {service} couldn't be reached or sent something unexpected.");
        }
        return null;
    }
}
