using Serilog;

namespace GreenHedges.Collectors.Common.Hosting;

/// <summary>Which collector this process is. The name appears in alert subjects, the User-Agent and the Windows service name.</summary>
/// <param name="Name">e.g. <c>LibreCollector</c>.</param>
public sealed record CollectorIdentity(string Name);

public static class CollectorHost
{
    /// <summary>
    /// The host builder every collector starts from: config from the app directory, Serilog from
    /// configuration, systemd (or Windows service) integration, <see cref="TimeProvider.System"/> and
    /// the <see cref="CollectorIdentity"/>.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args, string collectorName)
    {
        // Config (appsettings.json) always loads from the app directory. Data (database, logs) goes in the
        // working directory, so the install can stay read-only (systemd WorkingDirectory=/var/lib/...).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Runs as a Windows Service or under systemd; does nothing when run from a console.
        builder.Services.AddWindowsService(options => options.ServiceName = collectorName);
        builder.Services.AddSystemd();

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new CollectorIdentity(collectorName));
        return builder;
    }
}
