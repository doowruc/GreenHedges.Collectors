using System.Net;
using GreenHedges.Collectors.Libre.LibreLinkUp;
using GreenHedges.Collectors.Libre.LibreView;
using GreenHedges.Collectors.Libre.Nightscout;
using GreenHedges.Collectors.Libre.Storage;
using GreenHedges.Collectors.Libre.Worker;
using GreenHedges.Collectors.Libre.Worker.Alerts;
using GreenHedges.Collectors.Libre.Worker.Backups;
using GreenHedges.Collectors.Libre.Worker.Publishing;
using Microsoft.Extensions.Options;
using Serilog;

// Config (appsettings.json) always loads from the app directory. Data (database, logs) goes in the
// working directory, so the install can stay read-only (systemd WorkingDirectory=/var/lib/...).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Runs as a Windows Service or under systemd; does nothing when run from a console.
builder.Services.AddWindowsService(options => options.ServiceName = "LibreCollector");
builder.Services.AddSystemd();

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<LibreLinkUpOptions>()
    .BindConfiguration(LibreLinkUpOptions.SectionName)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Email) && !string.IsNullOrWhiteSpace(o.Password),
        "LibreLinkUp:Email and LibreLinkUp:Password must be set (user secrets or environment variables).")
    .ValidateOnStart();
builder.Services.AddOptions<CollectorOptions>().BindConfiguration(CollectorOptions.SectionName);
builder.Services.AddOptions<ArchiveOptions>().BindConfiguration(ArchiveOptions.SectionName);
builder.Services.AddOptions<AlertOptions>()
    .BindConfiguration(AlertOptions.SectionName)
    .Validate(
        o => !o.EmailEnabled || (!string.IsNullOrWhiteSpace(o.From) && o.To.Length > 0),
        "Alerts:From and Alerts:To must be set when Alerts:ResendApiKey is set.")
    .ValidateOnStart();
builder.Services.AddOptions<BackupOptions>().BindConfiguration(BackupOptions.SectionName);
// Not validated at startup: a bad off-site setting is reported by OffsiteBackupService (log + immediate
// alert) instead of stopping the collector.
builder.Services.AddOptions<OffsiteOptions>().BindConfiguration(OffsiteOptions.SectionName);
builder.Services.AddOptions<NightscoutOptions>()
    .BindConfiguration(NightscoutOptions.SectionName)
    .Validate(
        o => !o.Enabled
            || (Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _) && o.ApiSecret is { Length: >= 12 } && o.BatchSize > 0),
        "Nightscout:BaseUrl must be an absolute URL, and Nightscout:ApiSecret at least 12 characters, when publishing is on.")
    .ValidateOnStart();
builder.Services.AddOptions<HeartbeatOptions>()
    .BindConfiguration(HeartbeatOptions.SectionName)
    .Validate(
        o => string.IsNullOrWhiteSpace(o.PingUrl)
            || (Uri.TryCreate(o.PingUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps),
        "Heartbeat:PingUrl must be an absolute https URL.")
    .ValidateOnStart();

// The client holds the auth session, so it's a singleton with a long-lived HttpClient.
// PooledConnectionLifetime picks up DNS changes without needing IHttpClientFactory.
builder.Services.AddSingleton(services => new LibreLinkUpClient(
    new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    },
    services.GetRequiredService<IOptions<LibreLinkUpOptions>>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILogger<LibreLinkUpClient>>()));

builder.Services.AddSingleton(services =>
{
    var path = services.GetRequiredService<IOptions<ArchiveOptions>>().Value.DatabasePath;
    return new GlucoseArchive(Path.GetFullPath(path));
});

// Email via Resend if an API key is configured; otherwise alerts are only logged.
if (builder.Configuration.GetSection(AlertOptions.SectionName).Get<AlertOptions>()?.EmailEnabled == true)
{
    builder.Services.AddSingleton<IAlertSender>(services => new ResendAlertSender(
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        },
        services.GetRequiredService<IOptions<AlertOptions>>()));
}
else
{
    builder.Services.AddSingleton<IAlertSender, LoggingAlertSender>();
}

builder.Services.AddSingleton<CollectorHealth>();
builder.Services.AddSingleton(services => new AlertStateStore(
    Path.GetFullPath(services.GetRequiredService<IOptions<AlertOptions>>().Value.StateFile),
    services.GetRequiredService<ILogger<AlertStateStore>>()));
builder.Services.AddSingleton<SensorTracker>();
builder.Services.AddHostedService<CollectorService>();
builder.Services.AddHostedService<AlertMonitorService>();
builder.Services.AddHostedService<BackupService>();
builder.Services.AddSingleton<IBackupEncryptor>(services =>
{
    var o = services.GetRequiredService<IOptions<OffsiteOptions>>().Value;
    return new AgeEncryptor(o.AgePath, o.AgeRecipient!);
});
builder.Services.AddSingleton<IOffsiteStore>(services =>
    new S3OffsiteStore(services.GetRequiredService<IOptions<OffsiteOptions>>().Value));
builder.Services.AddHostedService<OffsiteBackupService>();

builder.Services.AddSingleton<PublishSignal>();
builder.Services.AddSingleton(services => new NightscoutClient(
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(60),
    },
    services.GetRequiredService<IOptions<NightscoutOptions>>()));
builder.Services.AddSingleton<NightscoutPublisher>();
builder.Services.AddHostedService<NightscoutPublisherService>();
builder.Services.AddHostedService(services => new HeartbeatService(
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(15),
    },
    services.GetRequiredService<CollectorHealth>(),
    services.GetRequiredService<IOptions<HeartbeatOptions>>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILogger<HeartbeatService>>()));

var host = builder.Build();

// `--test-alert` sends one alert through the configured sender and exits, to check the Resend setup.
if (args.Contains("--test-alert"))
{
    await host.Services.GetRequiredService<IAlertSender>().SendAsync(
        "[LibreCollector] Test alert",
        $"Test alert from {Environment.MachineName} at {DateTimeOffset.UtcNow:u}. If you can read this, alerts work.",
        CancellationToken.None);
    Console.WriteLine("Test alert sent.");
    return 0;
}

// Create or migrate the archive before any hosted service touches it.
var archive = host.Services.GetRequiredService<GlucoseArchive>();
await archive.InitialiseAsync(CancellationToken.None);

// `--import-libreview <file.csv> [--time-zone <IANA id>] [--date-format <format>]` imports a LibreView
// export into the archive and exits. Safe to run while the service is running, and to re-run.
var importIndex = Array.IndexOf(args, "--import-libreview");
if (importIndex >= 0)
{
    string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

    if (importIndex + 1 >= args.Length || !File.Exists(args[importIndex + 1]))
    {
        Console.Error.WriteLine("Usage: GreenHedges.Collectors.Libre.Worker --import-libreview <file.csv> [--time-zone Europe/London] [--date-format dd-MM-yyyy HH:mm]");
        Console.Error.WriteLine(importIndex + 1 < args.Length ? $"File not found: {args[importIndex + 1]}" : "No file given.");
        return 2;
    }

    var zone = TimeZoneInfo.FindSystemTimeZoneById(Option("--time-zone") ?? "Europe/London");
    var importer = new LibreViewImporter(
        archive,
        TimeProvider.System,
        host.Services.GetRequiredService<ILogger<LibreViewImporter>>());
    var result = await importer.ImportAsync(
        args[importIndex + 1],
        zone,
        Option("--date-format") ?? LibreViewImporter.DefaultTimestampFormat,
        CancellationToken.None);

    Console.WriteLine($"""
        LibreView import ({zone.Id})
          Rows read:                {result.RowsRead:N0}
          Historic readings:        {result.ReadingsImported:N0} imported, {result.ReadingsAlreadyPresent:N0} already present
          Scan readings:            {result.ScansImported:N0} imported, {result.ScansAlreadyPresent:N0} already present
          Events (insulin, carbs…): {result.EventsImported:N0} imported, {result.EventsAlreadyPresent:N0} already present
          Skipped:                  {result.SkippedAmbiguousTime:N0} ambiguous time (clock change), {result.SkippedInvalidTime:N0} invalid time, {result.SkippedEmpty:N0} empty, {result.SkippedUnparseable:N0} unparseable
          Span (UTC):               {result.FirstUtc:u} to {result.LastUtc:u}
        """);
    return 0;
}

await host.RunAsync();
return 0;
