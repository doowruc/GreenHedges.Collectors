using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker;

/// <summary>
/// Keeps rtl_433 running and archives every line it writes, as it arrives, before anything else is done
/// with it. Logs new devices, and a summary every <see cref="SummaryInterval"/> rather than every message
/// (one arrives every 12 s).
/// </summary>
public sealed class CollectorService(
    Rtl433Supervisor supervisor,
    WeatherArchive archive,
    CollectorHealth health,
    IOptions<StationOptions> stationOptions,
    TimeProvider time,
    ILogger<CollectorService> logger) : BackgroundService
{
    internal static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(15);

    private readonly StationOptions _station = stationOptions.Value;
    private readonly HashSet<(string Model, string DeviceId)> _knownDevices = [];
    private DateTimeOffset _summaryFrom;
    private int _messages;
    private int _stationMessages;
    private bool _stationSeen;
    private WeatherFields? _latestTemperature;
    private WeatherFields? _latestRain;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadKnownDevicesAsync(stoppingToken);

        if (_station.DeviceIds.Length == 0)
        {
            logger.LogWarning(
                "Station:DeviceIds is not set: every message is archived, but none is treated as the station's. " +
                "Find the station's id in the log (\"New device heard\") and set it");
        }
        else
        {
            logger.LogInformation("Station: {Model} {DeviceIds}", _station.Model, string.Join(", ", _station.DeviceIds));
        }

        _summaryFrom = time.GetUtcNow();
        await supervisor.RunAsync(line => HandleLineAsync(line, stoppingToken), stoppingToken);
    }

    internal async Task LoadKnownDevicesAsync(CancellationToken cancellationToken)
    {
        foreach (var device in await archive.GetDevicesAsync(cancellationToken))
        {
            _knownDevices.Add((device.Model, device.DeviceId));
        }
    }

    /// <summary>Archives one line. Never throws (except on shutdown): failures go to health and the log.</summary>
    internal async Task HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        var receivedAt = time.GetUtcNow();
        var message = Rtl433Message.Parse(line);
        var isStation = _station.IsStation(message.Model, message.DeviceId);

        try
        {
            await archive.AppendAsync(message, receivedAt, isStation, cancellationToken);
            health.RecordArchived(receivedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            health.RecordArchiveFailure(ex);
            logger.LogError(ex, "Couldn't archive a line from rtl_433: {Line}", line);
            return;
        }

        _messages++;
        if (!message.IsValidJson)
        {
            logger.LogWarning("rtl_433 wrote a line that isn't JSON (archived, flagged): {Line}", line);
        }
        else if (message is { Model: { } model, DeviceId: { } deviceId } && _knownDevices.Add((model, deviceId)))
        {
            logger.LogInformation("New device heard: {Model} id {DeviceId}{Station}", model, deviceId, isStation ? " (the station)" : "");
        }

        if (isStation && message.Weather is { } weather)
        {
            _stationMessages++;
            health.RecordStationMessage(receivedAt, message.DeviceId!, weather);
            if (weather.TemperatureC is not null)
            {
                _latestTemperature = weather;
            }

            if (weather.RainMm is not null)
            {
                _latestRain = weather;
            }

            if (!_stationSeen)
            {
                _stationSeen = true;
                logger.LogInformation("Station heard: {Model} id {DeviceId}", message.Model, message.DeviceId);
            }
        }

        if (receivedAt - _summaryFrom >= SummaryInterval)
        {
            LogSummary(receivedAt);
        }
    }

    private void LogSummary(DateTimeOffset now)
    {
        var wind = _latestRain ?? _latestTemperature;
        logger.LogInformation(
            "Last {Minutes:N0} min: {Messages} messages, {StationMessages} from the station. Latest: {Temperature} °C, {Humidity} %, " +
            "wind {WindAvg}/{WindMax} m/s from {WindDir}°, rain total {Rain} mm, battery {Battery}",
            (now - _summaryFrom).TotalMinutes,
            _messages,
            _stationMessages,
            _latestTemperature?.TemperatureC?.ToString("0.0") ?? "?",
            _latestTemperature?.Humidity?.ToString("0") ?? "?",
            wind?.WindAvgMs?.ToString("0.0") ?? "?",
            wind?.WindMaxMs?.ToString("0.0") ?? "?",
            wind?.WindDirDeg?.ToString("0") ?? "?",
            _latestRain?.RainMm?.ToString("0.0") ?? "?",
            _latestTemperature?.BatteryOk switch { 1 => "OK", 0 => "LOW", _ => "?" });

        _summaryFrom = now;
        _messages = 0;
        _stationMessages = 0;
    }
}
