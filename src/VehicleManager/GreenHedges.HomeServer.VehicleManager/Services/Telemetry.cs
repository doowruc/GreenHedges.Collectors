using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// OpenTelemetry logs, traces and metrics, sent over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set: by
/// <c>aspire run</c> (GreenHedges.HomeServer.VehicleManager.AppHost) for its dashboard, or by hand for another destination. The standard
/// <c>OTEL_*</c> settings (endpoint, headers, protocol, service name) configure it, so the destination can change
/// without code. Without an endpoint (plain <c>dotnet watch</c>, the tests) nothing is set up and logs go only to the
/// console. Nothing personal goes out: logs carry Auth0 user ids, never emails or registrations, and the DVSA's
/// request URLs (which end in the registration) are redacted in traces (<see cref="RedactUrl"/>).
/// </summary>
public static partial class Telemetry
{
    public const string ServiceName = "VehicleManager";

    /// <summary>The app's own spans, e.g. one per nightly job run and per user within it.</summary>
    public static readonly ActivitySource Source = new(ServiceName);

    public static bool IsExporting(IConfiguration configuration) =>
        configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 };

    public static void AddTelemetry(this WebApplicationBuilder builder)
    {
        if (!IsExporting(builder.Configuration)) return;

        // Through ILogger, so every existing log call is included.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(ServiceName)
                // Pages and form posts, not static files (css, js, images).
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !Path.HasExtension(context.Request.Path.Value))
                // Resend, the DVLA and the DVSA.
                .AddHttpClientInstrumentation(options => options.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    if (request.RequestUri is { } url && RedactUrl(url) is var redacted && redacted != url.AbsoluteUri)
                        activity.SetTag("url.full", redacted);
                })
                // Azure SQL, with EF Core's parameterised commands (the text, not the values).
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .UseOtlpExporter();
    }

    /// <summary>A request URL as traces record it, with the registration at the end of a DVSA lookup replaced by "{registration}".</summary>
    public static string RedactUrl(Uri url) => RegistrationPath().Replace(url.AbsoluteUri, "$1{registration}");

    [GeneratedRegex(@"(/vehicles/registration/)[^/?#]+", RegexOptions.IgnoreCase)]
    private static partial Regex RegistrationPath();
}
