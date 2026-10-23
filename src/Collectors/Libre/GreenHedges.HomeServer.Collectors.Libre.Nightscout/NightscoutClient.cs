using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Nightscout;

/// <summary>Minimal Nightscout API v1 client: uploads <c>sgv</c> entries.</summary>
public sealed class NightscoutClient(HttpClient http, IOptions<NightscoutOptions> options)
{
    private readonly Uri _entriesUri = new(new Uri(options.Value.BaseUrl!.TrimEnd('/') + "/"), "api/v1/entries");
    private readonly string _apiSecretHash = HashSecret(options.Value.ApiSecret ?? "");

    /// <summary>
    /// Posts entries. Nightscout upserts on <c>sysTime</c> + <c>type</c>, so re-sending is harmless.
    /// Throws <see cref="HttpRequestException"/> on a non-success response.
    /// </summary>
    public async Task UploadAsync(IReadOnlyCollection<NightscoutEntry> entries, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _entriesUri);
        request.Headers.Add("api-secret", _apiSecretHash);
        request.Content = JsonContent.Create(entries);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Nightscout returned HTTP {(int)response.StatusCode} for {entries.Count} entries: {Truncate(body, 300)}",
                null,
                response.StatusCode);
        }
    }

    /// <summary>Nightscout expects the SHA-1 of API_SECRET, as lowercase hex, in the <c>api-secret</c> header.</summary>
    internal static string HashSecret(string secret) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
