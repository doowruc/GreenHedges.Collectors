using GreenHedges.Collectors.Common.Hosting;
using GreenHedges.Collectors.Weather.Rtl433;
using GreenHedges.Collectors.Weather.Storage;
using GreenHedges.Collectors.Weather.Worker;
using Microsoft.Extensions.Options;

var builder = CollectorHost.CreateBuilder(args, "WeatherCollector");

builder.Services.AddOptions<Rtl433Options>()
    .BindConfiguration(Rtl433Options.SectionName)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Path) && o.SilenceTimeout > TimeSpan.Zero && o.MinRestartDelay > TimeSpan.Zero
            && o.MaxRestartDelay >= o.MinRestartDelay,
        "Rtl433:Path must be set, and the timeouts and restart delays positive (MaxRestartDelay >= MinRestartDelay).")
    .ValidateOnStart();
builder.Services.AddOptions<StationOptions>().BindConfiguration(StationOptions.SectionName);
builder.Services.AddOptions<ArchiveOptions>().BindConfiguration(ArchiveOptions.SectionName);

builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<Rtl433Options>>().Value);
builder.Services.AddSingleton<ReceiverHealth>();
builder.Services.AddSingleton<Rtl433Runner>();
builder.Services.AddSingleton<Rtl433Supervisor>();
builder.Services.AddSingleton(services =>
    new WeatherArchive(Path.GetFullPath(services.GetRequiredService<IOptions<ArchiveOptions>>().Value.DatabasePath)));
builder.Services.AddSingleton<CollectorHealth>();
builder.Services.AddHostedService<CollectorService>();

var host = builder.Build();

// Create or migrate the archive before any hosted service touches it.
var archive = host.Services.GetRequiredService<WeatherArchive>();
await archive.InitialiseAsync(CancellationToken.None);
host.Services.GetRequiredService<ILogger<WeatherArchive>>().LogInformation(
    "Archive ready (schema v{Version}): {Messages:N0} messages", WeatherArchive.SchemaVersion, await archive.CountMessagesAsync(CancellationToken.None));

await host.RunAsync();
return 0;
