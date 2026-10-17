using GreenHedges.Collectors.Common.Alerts;
using GreenHedges.Collectors.Common.Heartbeat;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Common.Hosting;

public static class CollectorServices
{
    /// <summary>
    /// Alert delivery: <see cref="AlertOptions"/> (validated at startup), email via Resend if an API key
    /// is configured (otherwise alerts are only logged), the persisted <see cref="AlertStateStore"/> and
    /// the <see cref="AlertBook"/>. The collector adds its own <see cref="AlertMonitorService"/>.
    /// </summary>
    public static HostApplicationBuilder AddAlerting(this HostApplicationBuilder builder)
    {
        builder.Services.AddOptions<AlertOptions>()
            .BindConfiguration(AlertOptions.SectionName)
            .Validate(
                o => !o.EmailEnabled || (!string.IsNullOrWhiteSpace(o.From) && o.To.Length > 0),
                "Alerts:From and Alerts:To must be set when Alerts:ResendApiKey is set.")
            .ValidateOnStart();

        if (builder.Configuration.GetSection(AlertOptions.SectionName).Get<AlertOptions>()?.EmailEnabled == true)
        {
            builder.Services.AddSingleton<IAlertSender>(services => new ResendAlertSender(
                new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                {
                    Timeout = TimeSpan.FromSeconds(30),
                },
                services.GetRequiredService<IOptions<AlertOptions>>(),
                services.GetRequiredService<CollectorIdentity>()));
        }
        else
        {
            builder.Services.AddSingleton<IAlertSender, LoggingAlertSender>();
        }

        builder.Services.AddSingleton(services => new AlertStateStore(
            Path.GetFullPath(services.GetRequiredService<IOptions<AlertOptions>>().Value.StateFile),
            services.GetRequiredService<ILogger<AlertStateStore>>()));
        builder.Services.AddSingleton<AlertBook>();
        return builder;
    }

    /// <summary>
    /// The off-machine heartbeat (<see cref="HeartbeatOptions"/>, validated at startup). The collector
    /// registers its <see cref="IHeartbeatSource"/>.
    /// </summary>
    public static HostApplicationBuilder AddHeartbeat(this HostApplicationBuilder builder)
    {
        builder.Services.AddOptions<HeartbeatOptions>()
            .BindConfiguration(HeartbeatOptions.SectionName)
            .Validate(
                o => string.IsNullOrWhiteSpace(o.PingUrl)
                    || (Uri.TryCreate(o.PingUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps),
                "Heartbeat:PingUrl must be an absolute https URL.")
            .ValidateOnStart();

        builder.Services.AddHostedService(services => new HeartbeatService(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(15),
            },
            services.GetRequiredService<IHeartbeatSource>(),
            services.GetRequiredService<IOptions<HeartbeatOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<HeartbeatService>>()));
        return builder;
    }

    /// <summary>
    /// Handles <c>--test-alert</c>: sends one alert through the configured sender, to check the Resend
    /// setup. Returns true if it ran, and the process should then exit.
    /// </summary>
    public static async Task<bool> TrySendTestAlertAsync(this IHost host, string[] args)
    {
        if (!args.Contains("--test-alert"))
        {
            return false;
        }

        var name = host.Services.GetRequiredService<CollectorIdentity>().Name;
        await host.Services.GetRequiredService<IAlertSender>().SendAsync(
            $"[{name}] Test alert",
            $"Test alert from {Environment.MachineName} at {DateTimeOffset.UtcNow:u}. If you can read this, alerts work.",
            CancellationToken.None);
        Console.WriteLine("Test alert sent.");
        return true;
    }
}
