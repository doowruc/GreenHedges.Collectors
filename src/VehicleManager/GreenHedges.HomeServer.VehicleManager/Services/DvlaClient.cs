using System.Net;
using System.Text.Json;

namespace VehicleManager.Services;

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
