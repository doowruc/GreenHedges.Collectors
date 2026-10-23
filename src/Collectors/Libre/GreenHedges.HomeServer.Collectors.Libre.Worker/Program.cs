using System.Net;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.LibreView;
using GreenHedges.HomeServer.Collectors.Libre.Nightscout;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using GreenHedges.HomeServer.Collectors.Libre.Worker;
using GreenHedges.HomeServer.Collectors.Libre.Worker.Alerts;
using GreenHedges.HomeServer.Collectors.Libre.Worker.Backups;
using GreenHedges.HomeServer.Collectors.Libre.Worker.Publishing;
using Microsoft.Extensions.Options;

var builder = CollectorHost.CreateBuilder(args, "LibreCollector");
builder.AddAlerting();
builder.AddHeartbeat();

builder.Services.AddOptions<LibreLinkUpOptions>()
    .BindConfiguration(LibreLinkUpOptions.SectionName)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Email) && !string.IsNullOrWhiteSpace(o.Password),
        "LibreLinkUp:Email and LibreLinkUp:Password must be set (user secrets or environment variables).")
    .ValidateOnStart();
builder.Services.AddOptions<CollectorOptions>().BindConfiguration(CollectorOptions.SectionName);
builder.Services.AddOptions<ArchiveOptions>().BindConfiguration(ArchiveOptions.SectionName);
builder.Services.AddOptions<LibreAlertOptions>().BindConfiguration(AlertOptions.SectionName);
builder.Services.AddOptions<NightscoutOptions>()
    .BindConfiguration(NightscoutOptions.SectionName)
    .Validate(
        o => !o.Enabled
            || (Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _) && o.ApiSecret is { Length: >= 12 } && o.BatchSize > 0),
        "Nightscout:BaseUrl must be an absolute URL, and Nightscout:ApiSecret at least 12 characters, when publishing is on.")
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

builder.Services.AddSingleton<CollectorHealth>();
builder.Services.AddSingleton<IHeartbeatSource>(services => services.GetRequiredService<CollectorHealth>());
builder.Services.AddSingleton<SensorTracker>();
builder.Services.AddHostedService<CollectorService>();
builder.Services.AddHostedService<LibreAlertMonitor>();
builder.AddBackups<ArchiveBackupSource>();

builder.Services.AddSingleton<PublishSignal>();
builder.Services.AddSingleton(services => new NightscoutClient(
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(60),
    },
    services.GetRequiredService<IOptions<NightscoutOptions>>()));
builder.Services.AddSingleton<NightscoutPublisher>();
builder.Services.AddHostedService<NightscoutPublisherService>();

var host = builder.Build();

// `--test-alert` sends one alert through the configured sender and exits, to check the Resend setup.
if (await host.TrySendTestAlertAsync(args))
{
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
        Console.Error.WriteLine("Usage: GreenHedges.HomeServer.Collectors.Libre.Worker --import-libreview <file.csv> [--time-zone Europe/London] [--date-format dd-MM-yyyy HH:mm]");
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
