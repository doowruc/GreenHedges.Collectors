using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>The DVSA MOT History API: a vehicle's MOT tests (and when its MOT runs out), by registration.</summary>
public class MotHistoryClient(HttpClient http, MotHistoryOptions options, MotHistoryTokenCache tokens)
{
    /// <returns>The vehicle's MOT history, or null if the DVSA has no vehicle with that registration.</returns>
    /// <exception cref="VehicleLookupException">The DVSA refused the request or failed.</exception>
    public async Task<MotHistoryVehicle?> GetAsync(string registration)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "v1/trade/vehicles/registration/" + Uri.EscapeDataString(VehicleLookup.Compact(registration)));
        request.Headers.Authorization = new("Bearer", await TokenAsync());
        request.Headers.Add("X-API-Key", options.ApiKey);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            // A rejected token is dropped, so the next lookup fetches a new one.
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Token = null;
            throw new VehicleLookupException($"The DVSA returned an error ({(int)response.StatusCode}).");
        }
        return await response.Content.ReadFromJsonAsync<MotHistoryVehicle>(JsonSerializerOptions.Web);
    }

    /// <summary>An access token from Microsoft Entra (client credentials), reused until a minute before it expires.</summary>
    private async Task<string> TokenAsync()
    {
        await tokens.Lock.WaitAsync();
        try
        {
            if (tokens.Token is { } cached && DateTimeOffset.UtcNow < tokens.Expires) return cached;

            using var response = await http.PostAsync(options.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["scope"] = options.Scope
            }));
            if (!response.IsSuccessStatusCode)
                throw new VehicleLookupException($"The DVSA sign-in was refused ({(int)response.StatusCode}). Check the MotHistory settings.");
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
                        ?? throw new VehicleLookupException("The DVSA sign-in returned no token.");
            tokens.Token = token.AccessToken;
            tokens.Expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn - 60));
            return token.AccessToken;
        }
        finally { tokens.Lock.Release(); }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
