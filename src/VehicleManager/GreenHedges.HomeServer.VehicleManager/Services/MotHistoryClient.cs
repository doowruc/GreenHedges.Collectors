using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VehicleManager.Services;

/// <summary>
/// DVSA MOT History API credentials (<c>MotHistory:ClientId</c>, <c>ClientSecret</c>, <c>ApiKey</c> and
/// <c>TokenUrl</c>, all from the DVSA's registration email). Optional, but all or none: without them
/// there are no MOT history lookups.
/// </summary>
public sealed record MotHistoryOptions(string? ClientId, string? ClientSecret, string? ApiKey, string? TokenUrl, string Scope, string BaseUrl)
{
    public const string DefaultScope = "https://tapi.dvsa.gov.uk/.default";
    public const string DefaultBaseUrl = "https://history.mot.api.gov.uk/";

    public bool IsConfigured => ClientId is { Length: > 0 };

    /// <exception cref="InvalidOperationException">Only some of the credentials are set.</exception>
    public static MotHistoryOptions From(IConfiguration section)
    {
        string? Get(string key) => section[key] is { Length: > 0 } value ? value : null;
        var options = new MotHistoryOptions(Get("ClientId"), Get("ClientSecret"), Get("ApiKey"), Get("TokenUrl"),
            Get("Scope") ?? DefaultScope, Get("BaseUrl") ?? DefaultBaseUrl);
        string?[] credentials = [options.ClientId, options.ClientSecret, options.ApiKey, options.TokenUrl];
        if (credentials.Any(c => c is null) && credentials.Any(c => c is not null))
            throw new InvalidOperationException(
                "Set all of MotHistory:ClientId, MotHistory:ClientSecret, MotHistory:ApiKey and MotHistory:TokenUrl, or none of them.");
        return options;
    }
}

/// <summary>A vehicle's MOT history. A vehicle too new for an MOT has no tests, but a first-MOT due date.</summary>
public sealed record MotHistoryVehicle(string? Registration, DateOnly? MotTestDueDate, List<MotHistoryTest>? MotTests)
{
    /// <summary>When the MOT runs out: the latest test certificate's expiry, or the first MOT's due date.</summary>
    public DateOnly? MotDueDate => MotTests?.Max(t => t.ExpiryDate) ?? MotTestDueDate;
}

/// <summary>
/// A test as the DVSA sends it. <see cref="OdometerResultType"/> is READ, UNREADABLE or NO_ODOMETER; Northern
/// Ireland's tests (<see cref="DataSource"/> "DVA NI") come without defects.
/// </summary>
public sealed record MotHistoryTest(
    DateTimeOffset? CompletedDate, string? TestResult, DateOnly? ExpiryDate, string? OdometerValue, string? OdometerUnit,
    string? OdometerResultType, string? MotTestNumber, string? RegistrationAtTimeOfTest, string? DataSource, List<MotHistoryDefect>? Defects);

public sealed record MotHistoryDefect(string? Text, string? Type, bool? Dangerous);

/// <summary>Holds the DVSA access token between requests (they last an hour), so each lookup doesn't fetch a new one.</summary>
public sealed class MotHistoryTokenCache
{
    internal readonly SemaphoreSlim Lock = new(1, 1);
    internal string? Token;
    internal DateTimeOffset Expires;
}

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
