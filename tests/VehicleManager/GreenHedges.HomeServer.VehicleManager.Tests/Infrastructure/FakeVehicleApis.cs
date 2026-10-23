using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>
/// Stands in for the DVLA Vehicle Enquiry and DVSA MOT History APIs (and the DVSA's token endpoint),
/// so tests never call the real ones. Answers only for registrations added with <see cref="Add"/>;
/// tests share it, so each should use its own registration (<see cref="NewRegistration"/>).
/// It checks the credentials <see cref="AppFactory"/> configures.
/// </summary>
public sealed class FakeVehicleApis : HttpMessageHandler
{
    public const string ApiKey = "tests";
    public const string TokenUrl = "https://login.tests.invalid/tenant/oauth2/v2.0/token";
    private const string AccessToken = "fake-token";

    /// <summary>The first defect of the failed test the DVSA sends for every vehicle with tests (dated 01/01/2020, registered "OLD 1").</summary>
    public const string FailedTestDefect = "Nearside front headlamp aim too high";

    private static readonly ConcurrentDictionary<string, FakeVehicle> Vehicles = new();
    private int _tokenRequests;

    /// <summary>How many access tokens this handler has been asked for.</summary>
    public int TokenRequests => _tokenRequests;

    /// <summary>A registration no other test uses, e.g. "FK3A9 C1D2".</summary>
    public static string NewRegistration()
    {
        var id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"FK{id[..3]} {id[3..7]}";
    }

    public static string Add(string registration, FakeVehicle vehicle)
    {
        Vehicles[registration.Replace(" ", "")] = vehicle;
        return registration;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        if (url.AbsoluteUri == TokenUrl) return Token(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (url.AbsolutePath == "/vehicle-enquiry/v1/vehicles" && request.Method == HttpMethod.Post) return await DvlaAsync(request, cancellationToken);
        const string motPath = "/v1/trade/vehicles/registration/";
        if (url.AbsolutePath.StartsWith(motPath) && request.Method == HttpMethod.Get) return Dvsa(request, Uri.UnescapeDataString(url.AbsolutePath[motPath.Length..]));
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Token(string form)
    {
        Interlocked.Increment(ref _tokenRequests);
        return form.Contains("grant_type=client_credentials") && form.Contains("client_secret=tests")
            ? Json(HttpStatusCode.OK, new { token_type = "Bearer", expires_in = 3599, access_token = AccessToken })
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> DvlaAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValues("x-api-key", out var keys) || keys.Single() != ApiKey)
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var registration = body.GetProperty("registrationNumber").GetString()!;
        if (!Vehicles.TryGetValue(registration, out var v) || !v.AtDvla)
            return Json(HttpStatusCode.NotFound, new { errors = new[] { new { status = "404", code = "404", title = "Vehicle Not Found", detail = "Record for vehicle not found" } } });
        if (v.DvlaFails)
            return Json(HttpStatusCode.InternalServerError, new { errors = new[] { new { status = "500", title = "Internal Server Error", detail = "System error occurred" } } });
        return Json(HttpStatusCode.OK, new Dictionary<string, object?>
        {
            ["registrationNumber"] = registration,
            ["taxStatus"] = v.TaxStatus,
            ["taxDueDate"] = v.TaxDueDate?.ToString("yyyy-MM-dd"),
            ["motStatus"] = v.DvlaMotExpiryDate is null ? "No details held by DVLA" : "Valid",
            ["motExpiryDate"] = v.DvlaMotExpiryDate?.ToString("yyyy-MM-dd"),
            ["make"] = "FORD",
            ["colour"] = "BLUE"
        }.Where(p => p.Value is not null).ToDictionary());
    }

    private static HttpResponseMessage Dvsa(HttpRequestMessage request, string registration)
    {
        if (request.Headers.Authorization?.ToString() != $"Bearer {AccessToken}" ||
            !request.Headers.TryGetValues("X-API-Key", out var keys) || keys.Single() != ApiKey)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (!Vehicles.TryGetValue(registration, out var v) || !v.AtDvsa)
            return Json(HttpStatusCode.NotFound, new { errorCode = "MOTH-NP-01", errorMessage = "No data found" });

        if (v.FirstMotDueDate is { } first)
            return Json(HttpStatusCode.OK, new { registration, make = "FORD", motTestDueDate = first.ToString("yyyy-MM-dd"), hasOutstandingRecall = "Unknown" });

        // Passes with an advisory each, and a failed test with defects of its own.
        var tests = v.MotTestExpiryDates.Select((expiry, i) => (object)new
            {
                completedDate = expiry.AddYears(-1).ToDateTime(new TimeOnly(10, 30)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                testResult = "PASSED",
                expiryDate = expiry.ToString("yyyy-MM-dd"),
                odometerValue = (30000 + i * 8000).ToString(),
                odometerUnit = "MI",
                odometerResultType = "READ",
                motTestNumber = $"1234567890{i}",
                registrationAtTimeOfTest = registration,
                dataSource = "DVSA",
                defects = new[] { new { text = $"Advisory {i + 1}: tyre worn close to legal limit", type = "ADVISORY", dangerous = false } }
            })
            .Append(new
            {
                completedDate = "2020-01-01T09:00:00.000Z", testResult = "FAILED", odometerValue = "20000", odometerUnit = "MI",
                odometerResultType = "READ", motTestNumber = "99", registrationAtTimeOfTest = "OLD 1", dataSource = "DVSA",
                defects = new[]
                {
                    new { text = FailedTestDefect, type = "MAJOR", dangerous = false },
                    new { text = "Brake pipe corroded", type = "DANGEROUS", dangerous = true }
                }
            });
        return Json(HttpStatusCode.OK, new { registration, make = "FORD", hasOutstandingRecall = "No", motTests = tests });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };
}
