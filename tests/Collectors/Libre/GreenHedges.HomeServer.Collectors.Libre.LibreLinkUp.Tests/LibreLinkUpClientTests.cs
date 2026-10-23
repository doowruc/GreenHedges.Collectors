using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp.Tests;

public sealed class LibreLinkUpClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private readonly FakeHttpHandler _handler = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly LibreLinkUpOptions _options = new() { Email = "follower@example.com", Password = "secret" };

    private LibreLinkUpClient CreateClient() =>
        new(new HttpClient(_handler), Options.Create(_options), _time, NullLogger<LibreLinkUpClient>.Instance);

    private static string LoginOk(string token = "token-1", string userId = "user-123", DateTimeOffset? expires = null) =>
        $$"""
        {
          "status": 0,
          "data": {
            "user": { "id": "{{userId}}", "country": "GB" },
            "authTicket": { "token": "{{token}}", "expires": {{(expires ?? Now.AddHours(1)).ToUnixTimeSeconds()}}, "duration": 3600000 }
          }
        }
        """;

    private const string Measurement =
        """
        {
          "FactoryTimestamp": "10/16/2026 8:59:00 PM",
          "Timestamp": "10/16/2026 9:59:00 PM",
          "type": 1,
          "ValueInMgPerDl": 115,
          "TrendArrow": 3,
          "MeasurementColor": 1,
          "GlucoseUnits": 0,
          "Value": 6.4,
          "isHigh": false,
          "isLow": false
        }
        """;

    private static readonly string ConnectionsOk =
        $$"""
        {
          "status": 0,
          "data": [
            { "id": "conn-1", "patientId": "patient-1", "firstName": "Test", "lastName": "Patient", "glucoseMeasurement": {{Measurement}} }
          ]
        }
        """;

    [Fact]
    public async Task Login_follows_region_redirect_then_sends_auth_headers()
    {
        _handler
            .Reply("""{ "status": 0, "data": { "redirect": true, "region": "eu2" } }""")
            .Reply(LoginOk())
            .Reply(ConnectionsOk);

        var connections = await CreateClient().GetConnectionsAsync(CancellationToken.None);

        Assert.Single(connections);
        Assert.Collection(
            _handler.Requests,
            first => Assert.Equal("https://api.libreview.io/llu/auth/login", first.Uri.ToString()),
            second => Assert.Equal("https://api-eu2.libreview.io/llu/auth/login", second.Uri.ToString()),
            third =>
            {
                Assert.Equal("https://api-eu2.libreview.io/llu/connections", third.Uri.ToString());
                Assert.Equal("Bearer token-1", third.Header("Authorization"));
                Assert.Equal(LibreLinkUpClient.HashAccountId("user-123"), third.Header("account-id"));
                Assert.Equal("llu.android", third.Header("product"));
                Assert.Equal("5.0.1", third.Header("version"));
            });
    }

    [Fact]
    public async Task Login_sends_camel_case_credentials()
    {
        _handler.Reply(LoginOk()).Reply(ConnectionsOk);

        await CreateClient().GetConnectionsAsync(CancellationToken.None);

        using var body = JsonDocument.Parse(_handler.Requests[0].Body!);
        Assert.Equal("follower@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("secret", body.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public void Account_id_is_lowercase_sha256_hex_of_user_id()
    {
        // echo -n "user-123" | shasum -a 256
        Assert.Equal(
            "fcdec6df4d44dbc637c7c5b58efface52a7f8a88535423430255be0bb89bedd8",
            LibreLinkUpClient.HashAccountId("user-123"));
    }

    [Fact]
    public async Task Configured_region_skips_global_endpoint()
    {
        _options.Region = "EU";
        _handler.Reply(LoginOk()).Reply(ConnectionsOk);

        await CreateClient().GetConnectionsAsync(CancellationToken.None);

        Assert.Equal("https://api-eu.libreview.io/llu/auth/login", _handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Redirect_to_unexpected_region_is_refused()
    {
        _handler.Reply("""{ "status": 0, "data": { "redirect": true, "region": "evil.example.com/" } }""");

        await Assert.ThrowsAsync<LibreLinkUpException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Parses_current_measurement()
    {
        _handler.Reply(LoginOk()).Reply(ConnectionsOk);

        var connection = Assert.Single(await CreateClient().GetConnectionsAsync(CancellationToken.None));

        Assert.Equal("patient-1", connection.PatientId);
        var reading = Assert.IsType<GlucoseReading>(connection.Current);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 20, 59, 0, TimeSpan.Zero), reading.TimestampUtc);
        Assert.Equal("10/16/2026 9:59:00 PM", reading.TimestampLocal);
        Assert.Equal(115, reading.ValueMgPerDl);
        Assert.Equal(3, reading.TrendArrow);
        Assert.Equal(1, reading.MeasurementColor);
        Assert.False(reading.IsHigh);
        Assert.False(reading.IsLow);
        Assert.Contains("\"ValueInMgPerDl\": 115", reading.RawJson);
    }

    [Fact]
    public async Task Parses_sensor_details()
    {
        _handler.Reply(LoginOk()).Reply(
            $$"""
            {
              "status": 0,
              "data": [
                {
                  "patientId": "patient-1",
                  "sensor": { "deviceId": "", "sn": "0M00ABC1234", "a": 1791676800, "w": 60, "pt": 4, "s": false, "lj": false },
                  "glucoseMeasurement": {{Measurement}}
                }
              ]
            }
            """);

        var connection = Assert.Single(await CreateClient().GetConnectionsAsync(CancellationToken.None));

        var sensor = Assert.IsType<SensorInfo>(connection.Sensor);
        Assert.Equal("0M00ABC1234", sensor.Serial);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791676800), sensor.ActivatedAt);
        Assert.Equal(60, sensor.WarmupMinutes);
        Assert.Equal(4, sensor.ProductType);
        Assert.Contains("\"lj\": false", sensor.RawJson);
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "sensor": null""")]
    [InlineData(""", "sensor": { "sn": "", "a": 1791676800 }""")]
    public async Task Missing_or_blank_sensor_is_null(string sensorJson)
    {
        _handler.Reply(LoginOk()).Reply(
            $$"""{ "status": 0, "data": [ { "patientId": "patient-1"{{sensorJson}}, "glucoseMeasurement": {{Measurement}} } ] }""");

        var connection = Assert.Single(await CreateClient().GetConnectionsAsync(CancellationToken.None));

        Assert.Null(connection.Sensor);
        Assert.NotNull(connection.Current);
    }

    [Fact]
    public async Task Reuses_token_until_close_to_expiry_then_logs_in_again()
    {
        _handler
            .Reply(LoginOk("token-1"))
            .Reply(ConnectionsOk)
            .Reply(ConnectionsOk)
            .Reply(LoginOk("token-2", expires: Now.AddHours(2)))
            .Reply(ConnectionsOk);
        var client = CreateClient();

        await client.GetConnectionsAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(50));
        await client.GetConnectionsAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(6)); // inside the 5-minute refresh margin
        await client.GetConnectionsAsync(CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count(r => r.Uri.AbsolutePath == "/llu/auth/login"));
        Assert.Equal("Bearer token-2", _handler.Requests[^1].Header("Authorization"));
    }

    [Fact]
    public async Task Unauthorized_triggers_one_relogin_and_retry()
    {
        _handler
            .Reply(LoginOk("token-1"))
            .Reply("{}", HttpStatusCode.Unauthorized)
            .Reply(LoginOk("token-2"))
            .Reply(ConnectionsOk);

        var connections = await CreateClient().GetConnectionsAsync(CancellationToken.None);

        Assert.Single(connections);
        Assert.Equal("Bearer token-2", _handler.Requests[^1].Header("Authorization"));
    }

    [Fact]
    public async Task Repeated_unauthorized_is_not_retried_forever()
    {
        _handler
            .Reply(LoginOk("token-1"))
            .Reply("""{ "status": 401 }""", HttpStatusCode.Unauthorized)
            .Reply(LoginOk("token-2"))
            .Reply("""{ "status": 401 }""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<LibreLinkUpException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.HttpStatus);
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task Bad_credentials()
    {
        _handler.Reply("""{ "status": 2, "error": { "message": "Bad credentials" } }""");

        await Assert.ThrowsAsync<BadCredentialsException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Terms_of_use_step()
    {
        _handler.Reply(
            """{ "status": 4, "data": { "step": { "type": "tou", "componentName": "AcceptDocument" }, "authTicket": { "token": "short", "expires": 1, "duration": 1 } } }""");

        var ex = await Assert.ThrowsAsync<LoginStepRequiredException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Equal("tou", ex.StepType);
    }

    [Fact]
    public async Task Version_too_old()
    {
        _options.Version = "4.2.1";
        _handler.Reply("""{ "status": 920, "data": { "minimumVersion": "4.16.0" } }""", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<UnsupportedVersionException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Equal("4.16.0", ex.MinimumVersion);
        Assert.Equal("4.2.1", ex.ConfiguredVersion);
    }

    [Fact]
    public async Task Rate_limited_reports_retry_after()
    {
        _handler.Reply(
            """{ "status": 429, "data": { "code": 60 } }""",
            HttpStatusCode.TooManyRequests,
            r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10)));

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(10), ex.RetryAfter);
    }

    [Fact]
    public async Task Non_json_error_response()
    {
        _handler.Reply("<html>Bad gateway</html>", HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<LibreLinkUpException>(() => CreateClient().GetConnectionsAsync(CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadGateway, ex.HttpStatus);
    }

    [Fact]
    public async Task Graph_returns_current_and_history_and_skips_bad_items()
    {
        _handler
            .Reply(LoginOk())
            .Reply(
                $$"""
                {
                  "status": 0,
                  "data": {
                    "connection": { "patientId": "patient-1", "glucoseMeasurement": {{Measurement}} },
                    "activeSensors": [],
                    "graphData": [
                      { "FactoryTimestamp": "10/16/2026 8:30:00 AM", "Timestamp": "10/16/2026 9:30:00 AM", "type": 0, "ValueInMgPerDl": 101, "MeasurementColor": 1, "GlucoseUnits": 0, "Value": 5.6, "isHigh": false, "isLow": false },
                      { "FactoryTimestamp": "10/16/2026 8:45:00 AM", "Timestamp": "10/16/2026 9:45:00 AM", "type": 0, "ValueInMgPerDl": 250, "MeasurementColor": 3, "GlucoseUnits": 0, "Value": 13.9, "isHigh": true, "isLow": false },
                      { "Timestamp": "missing factory timestamp", "ValueInMgPerDl": 99 }
                    ]
                  }
                }
                """);

        var graph = await CreateClient().GetGraphAsync("patient-1", CancellationToken.None);

        Assert.Equal("/llu/connections/patient-1/graph", _handler.Requests[^1].Uri.AbsolutePath);
        Assert.NotNull(graph.Current);
        Assert.Collection(
            graph.History,
            first =>
            {
                Assert.Equal(new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero), first.TimestampUtc);
                Assert.Null(first.TrendArrow);
            },
            second =>
            {
                Assert.Equal(250, second.ValueMgPerDl);
                Assert.True(second.IsHigh);
            });
    }

    [Theory]
    [InlineData("1/2/2026 3:04:05 AM", 2026, 1, 2, 3, 4, 5)]
    [InlineData("12/31/2026 11:59:59 PM", 2026, 12, 31, 23, 59, 59)]
    [InlineData("3/29/2026 1:30:00 AM", 2026, 3, 29, 1, 30, 0)] // UK clocks go forward at this local time; UTC is unaffected
    public void Factory_timestamp_is_parsed_as_utc(string text, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(JsonHelpers.TryParseFactoryTimestamp(text, out var utc));
        Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero), utc);
    }
}
