namespace GreenHedges.HomeServer.VehicleManager.Services;

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
