namespace GreenHedges.HomeServer.VehicleManager.Services;

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
