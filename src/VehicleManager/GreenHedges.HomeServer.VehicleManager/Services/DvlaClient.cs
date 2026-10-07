using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VehicleManager.Services;

/// <summary>
/// The DVLA Vehicle Enquiry Service key (<c>Dvla:ApiKey</c>, optional: without it there are no DVLA
/// lookups). <c>Dvla:BaseUrl</c> can point at DVLA's test (UAT) service instead.
/// </summary>
public sealed record DvlaOptions(string? ApiKey, string BaseUrl)
{
    public const string DefaultBaseUrl = "https://driver-vehicle-licensing.api.gov.uk/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static DvlaOptions From(IConfiguration section) =>
        new(section["ApiKey"], section["BaseUrl"] is { Length: > 0 } url ? url : DefaultBaseUrl);
}

/// <summary>What the DVLA holds about a vehicle's tax and MOT. Dates are YYYY-MM-DD in the response.</summary>
public sealed record DvlaVehicle(string? RegistrationNumber, string? TaxStatus, DateOnly? TaxDueDate, string? MotStatus, DateOnly? MotExpiryDate)
{
    /// <summary>Declared off the road, so no tax is due.</summary>
    public bool IsSorn => TaxStatus == "SORN";
}

/// <summary>The DVLA Vehicle Enquiry Service: tax status and due date, and MOT expiry, by registration.</summary>
public class DvlaClient(HttpClient http, DvlaOptions options)
{
    /// <returns>The vehicle, or null if the DVLA has no vehicle with that registration.</returns>
    /// <exception cref="VehicleLookupException">The DVLA refused the request or failed.</exception>
    public async Task<DvlaVehicle?> GetAsync(string registration)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "vehicle-enquiry/v1/vehicles")
        {
            Content = JsonContent.Create(new { registrationNumber = VehicleLookup.Compact(registration) })
        };
        request.Headers.Add("x-api-key", options.ApiKey);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new VehicleLookupException($"The DVLA returned an error ({(int)response.StatusCode}{await ErrorAsync(response)}).");
        return await response.Content.ReadFromJsonAsync<DvlaVehicle>(JsonSerializerOptions.Web);
    }

    /// <summary>The DVLA's own description of an error, e.g. ": Invalid format for field - vehicle registration number".</summary>
    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<DvlaErrors>(JsonSerializerOptions.Web);
            return body?.Errors?.FirstOrDefault() is { } e ? ": " + (e.Detail ?? e.Title) : "";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return ""; }
    }

    private sealed record DvlaErrors(List<DvlaError>? Errors);
    private sealed record DvlaError(string? Title, string? Detail);
}

/// <summary>A lookup service refused a request or failed; the message says why, for showing to the user.</summary>
public sealed class VehicleLookupException(string message) : Exception(message);
