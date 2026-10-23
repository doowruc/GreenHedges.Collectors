using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;

/// <summary>
/// Client for the unofficial LibreLinkUp follower API. Thread-safe: one shared instance holds the
/// auth session and logs in again shortly before the ticket expires.
/// </summary>
public sealed partial class LibreLinkUpClient
{
    private readonly HttpClient _http;
    private readonly LibreLinkUpOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LibreLinkUpClient> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private Uri _baseUri;
    private volatile Session? _session;

    public LibreLinkUpClient(
        HttpClient http,
        IOptions<LibreLinkUpOptions> options,
        TimeProvider time,
        ILogger<LibreLinkUpClient> logger)
    {
        _http = http;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _baseUri = string.IsNullOrWhiteSpace(_options.Region)
            ? new Uri("https://api.libreview.io/")
            : RegionBaseUri(_options.Region);
    }

    public async Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        using var document = await SendAuthenticatedAsync("llu/connections", cancellationToken);

        var connections = new List<Connection>();
        if (document.RootElement.Get("data") is { ValueKind: JsonValueKind.Array } data)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.GetString("patientId") is not { } patientId)
                {
                    continue;
                }

                connections.Add(new Connection(
                    patientId,
                    item.GetString("firstName"),
                    item.GetString("lastName"),
                    ParseMeasurementOrWarn(item.Get("glucoseMeasurement")),
                    JsonHelpers.ParseSensor(item.Get("sensor"))));
            }
        }

        return connections;
    }

    public async Task<GraphData> GetGraphAsync(string patientId, CancellationToken cancellationToken)
    {
        using var document = await SendAuthenticatedAsync(
            $"llu/connections/{Uri.EscapeDataString(patientId)}/graph",
            cancellationToken);

        var data = document.RootElement.Get("data");
        var current = ParseMeasurementOrWarn(data?.Get("connection")?.Get("glucoseMeasurement"));

        var history = new List<GlucoseReading>();
        if (data?.Get("graphData") is { ValueKind: JsonValueKind.Array } graphData)
        {
            foreach (var item in graphData.EnumerateArray())
            {
                if (ParseMeasurementOrWarn(item) is { } reading)
                {
                    history.Add(reading);
                }
            }
        }

        return new GraphData(current, history);
    }

    private GlucoseReading? ParseMeasurementOrWarn(JsonElement? element)
    {
        if (element is not { } measurement)
        {
            return null;
        }

        var reading = JsonHelpers.ParseMeasurement(measurement);
        if (reading is null)
        {
            _logger.LogWarning("Skipping unparseable measurement: {Json}", measurement.GetRawText());
        }

        return reading;
    }

    private async Task<JsonDocument> SendAuthenticatedAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var session = await GetSessionAsync(cancellationToken);

            using var request = CreateRequest(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            request.Headers.Add("account-id", session.AccountIdHash);

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                // Token revoked or expired early. Drop it and log in again, once.
                _logger.LogInformation("LibreLinkUp returned 401; logging in again");
                Interlocked.CompareExchange(ref _session, null, session);
                continue;
            }

            return await ReadEnvelopeAsync(response, cancellationToken);
        }
    }

    private async Task<Session> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { } current && !IsExpiring(current))
        {
            return current;
        }

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is { } refreshed && !IsExpiring(refreshed))
            {
                return refreshed;
            }

            var session = await LoginAsync(cancellationToken);
            _session = session;
            return session;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private bool IsExpiring(Session session) =>
        _time.GetUtcNow() >= session.ExpiresAt - _options.TokenRefreshMargin;

    private async Task<Session> LoginAsync(CancellationToken cancellationToken)
    {
        var redirected = false;
        while (true)
        {
            using var request = CreateRequest(HttpMethod.Post, "llu/auth/login");
            request.Content = JsonContent.Create(new LoginRequest(_options.Email, _options.Password));

            using var response = await _http.SendAsync(request, cancellationToken);
            using var document = await ReadEnvelopeAsync(response, cancellationToken);
            var data = document.RootElement.Get("data");

            if (data?.GetBoolean("redirect") == true)
            {
                var region = data.Value.GetString("region");
                if (redirected || region is null || !RegionPattern().IsMatch(region))
                {
                    throw new LibreLinkUpException(
                        $"Unexpected login redirect (region '{region}', already redirected: {redirected}).",
                        0,
                        response.StatusCode);
                }

                _logger.LogInformation(
                    "LibreLinkUp redirected login to region {Region}. Set LibreLinkUp:Region to skip this step",
                    region);
                _baseUri = RegionBaseUri(region);
                redirected = true;
                continue;
            }

            var userId = data?.Get("user")?.GetString("id");
            var ticket = data?.Get("authTicket");
            var token = ticket?.GetString("token");
            var expires = ticket?.GetInt64("expires");
            if (userId is null || token is null || expires is null)
            {
                throw new LibreLinkUpException("Login response is missing user.id or authTicket.", 0, response.StatusCode);
            }

            var session = new Session(token, DateTimeOffset.FromUnixTimeSeconds(expires.Value), HashAccountId(userId));
            _logger.LogInformation(
                "Logged in to LibreLinkUp at {BaseUri}; token expires {ExpiresAt:u}",
                _baseUri,
                session.ExpiresAt);
            return session;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.Add("product", _options.Product);
        request.Headers.Add("version", _options.Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.UserAgent.TryParseAdd($"LibreLinkUp/{_options.Version}");
        return request;
    }

    /// <summary>
    /// Reads the <c>{ status, data, error }</c> envelope and maps error statuses to exceptions.
    /// The caller owns the returned document.
    /// </summary>
    private async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new RateLimitedException(GetRetryAfter(response));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LibreLinkUpException(
                $"LibreLinkUp returned a non-JSON response (HTTP {(int)response.StatusCode}).",
                null,
                response.StatusCode,
                ex);
        }

        try
        {
            var root = document.RootElement;
            var status = root.GetInt32("status");
            var data = root.Get("data");

            switch (status)
            {
                case 0 when response.IsSuccessStatusCode:
                    return document;
                case 2:
                    throw new BadCredentialsException(2, response.StatusCode);
                case 4:
                    throw new LoginStepRequiredException(
                        data?.Get("step")?.GetString("type") ?? "unknown",
                        response.StatusCode);
                case 920:
                    throw new UnsupportedVersionException(
                        _options.Version,
                        data?.GetString("minimumVersion"),
                        response.StatusCode);
                case 429:
                    throw new RateLimitedException(GetRetryAfter(response), 429);
                default:
                    var message = root.Get("error")?.GetString("message");
                    throw new LibreLinkUpException(
                        $"LibreLinkUp returned status {status?.ToString() ?? "(none)"} (HTTP {(int)response.StatusCode}): {message ?? "no message"}",
                        status,
                        response.StatusCode);
            }
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - _time.GetUtcNow(),
            _ => null,
        };

    private static Uri RegionBaseUri(string region) => new($"https://api-{region.ToLowerInvariant()}.libreview.io/");

    internal static string HashAccountId(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));

    // Regions seen so far are short codes like "eu", "eu2", "us", "ap". Anything else is refused,
    // so a response can't send us to an arbitrary host.
    [GeneratedRegex("^[a-zA-Z0-9]{1,10}$")]
    private static partial Regex RegionPattern();

    private sealed record LoginRequest(string Email, string Password);

    private sealed record Session(string Token, DateTimeOffset ExpiresAt, string AccountIdHash);
}
