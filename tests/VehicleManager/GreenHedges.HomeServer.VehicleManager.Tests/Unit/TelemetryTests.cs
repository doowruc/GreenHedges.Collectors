using Microsoft.Extensions.Configuration;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class TelemetryTests
{
    [Theory]
    [InlineData("https://history.mot.api.gov.uk/v1/trade/vehicles/registration/AB12CDE",
                "https://history.mot.api.gov.uk/v1/trade/vehicles/registration/{registration}")]
    [InlineData("https://history.mot.api.gov.uk/v1/trade/vehicles/registration/AB12%20CDE?x=1",
                "https://history.mot.api.gov.uk/v1/trade/vehicles/registration/{registration}?x=1")]
    [InlineData("https://driver-vehicle-licensing.api.gov.uk/vehicle-enquiry/v1/vehicles",
                "https://driver-vehicle-licensing.api.gov.uk/vehicle-enquiry/v1/vehicles")]   // the DVLA's is in the body, not the URL
    public void Registrations_are_taken_out_of_traced_URLs(string url, string traced) =>
        Assert.Equal(traced, Telemetry.RedactUrl(new Uri(url)));

    [Fact]
    public void Telemetry_is_only_sent_with_an_endpoint()
    {
        static IConfiguration Settings(string? endpoint) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint }).Build();
        Assert.False(Telemetry.IsExporting(Settings(null)));
        Assert.False(Telemetry.IsExporting(Settings("")));
        Assert.True(Telemetry.IsExporting(Settings("https://localhost:21080")));
    }
}
