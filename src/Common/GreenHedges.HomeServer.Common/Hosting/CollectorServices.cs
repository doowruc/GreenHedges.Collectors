using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Heartbeat;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Common.Hosting;

/// <summary>
/// The shared services every collector, and Vehicle Manager, registers. They take any <see cref="IHostApplicationBuilder"/>:
/// a worker's <see cref="HostApplicationBuilder"/> or a web app's <c>WebApplicationBuilder</c>. A host that doesn't start from
/// <see cref="CollectorHost.CreateBuilder"/> registers a <see cref="TimeProvider"/> and a <see cref="CollectorIdentity"/> itself.
/// </summary>
public static class CollectorServices
{
    /// <summary>
    /// Alert delivery: <see cref="AlertOptions"/> (validated at startup), email via Resend if an API key
    /// is configured (otherwise alerts are only logged), the persisted <see cref="AlertStateStore"/> and
    /// the <see cref="AlertBook"/>. The collector adds its own <see cref="AlertMonitorService"/>.
    /// </summary>
    public static TBuilder AddAlerting<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
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
    public static TBuilder AddHeartbeat<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
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
    /// Nightly local backups of <typeparamref name="TSource"/> and encrypted off-site copies, with their
    /// health and the <see cref="BackupMonitor"/> alerts. Off-site settings aren't validated at startup: a bad
    /// one is reported by <see cref="OffsiteBackupService"/> (log and immediate alert) instead of stopping
    /// the collector.
    /// </summary>
    public static IHostApplicationBuilder AddBackups<TSource>(this IHostApplicationBuilder builder)
        where TSource : class, IBackupSource
    {
        builder.Services.AddOptions<BackupOptions>().BindConfiguration(BackupOptions.SectionName);
        builder.Services.AddOptions<OffsiteOptions>().BindConfiguration(OffsiteOptions.SectionName);

        builder.Services.AddSingleton<IBackupSource, TSource>();
        builder.Services.AddSingleton<BackupHealth>();
        builder.Services.AddSingleton<BackupMonitor>();
        builder.Services.AddHostedService<BackupService>();
        builder.Services.AddSingleton<IBackupEncryptor>(services =>
        {
            var o = services.GetRequiredService<IOptions<OffsiteOptions>>().Value;
            return new AgeEncryptor(o.AgePath, o.AgeRecipient!);
        });
        builder.Services.AddSingleton<IOffsiteStore>(services =>
            new S3OffsiteStore(services.GetRequiredService<IOptions<OffsiteOptions>>().Value));
        builder.Services.AddHostedService<OffsiteBackupService>();
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
