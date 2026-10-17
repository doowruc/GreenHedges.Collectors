using System.Text.Json;

namespace GreenHedges.Collectors.Weather.Rtl433;

/// <summary>
/// The weather and signal fields of a message, by rtl_433's common field names, all optional: a Bresser
/// 5-in-1 sends temperature and humidity in one message and the rain total in the next, with wind in both.
/// </summary>
/// <param name="TemperatureC"><c>temperature_C</c>, °C.</param>
/// <param name="Humidity"><c>humidity</c>, %.</param>
/// <param name="WindAvgMs"><c>wind_avg_m_s</c>, m/s.</param>
/// <param name="WindMaxMs"><c>wind_max_m_s</c> (gust), m/s.</param>
/// <param name="WindDirDeg"><c>wind_dir_deg</c>, degrees.</param>
/// <param name="RainMm"><c>rain_mm</c>: running total since the sensor's power-on, mm.</param>
/// <param name="BatteryOk"><c>battery_ok</c>: 1 OK, 0 low.</param>
/// <param name="Flags"><c>flags</c>: for the Bresser 6-in-1 decoder, 0 = temperature/humidity message, 1 = rain message.</param>
/// <param name="Startup"><c>startup</c>.</param>
/// <param name="Rssi"><c>rssi</c>, dB (needs <c>-M level</c>).</param>
/// <param name="Snr"><c>snr</c>, dB (needs <c>-M level</c>).</param>
public sealed record WeatherFields(
    double? TemperatureC,
    double? Humidity,
    double? WindAvgMs,
    double? WindMaxMs,
    double? WindDirDeg,
    double? RainMm,
    int? BatteryOk,
    int? Flags,
    int? Startup,
    double? Rssi,
    double? Snr)
{
    internal static WeatherFields From(JsonElement root) => new(
        Rtl433Message.Number(root, "temperature_C"),
        Rtl433Message.Number(root, "humidity"),
        Rtl433Message.Number(root, "wind_avg_m_s"),
        Rtl433Message.Number(root, "wind_max_m_s"),
        Rtl433Message.Number(root, "wind_dir_deg"),
        Rtl433Message.Number(root, "rain_mm"),
        Rtl433Message.Integer(root, "battery_ok"),
        Rtl433Message.Integer(root, "flags"),
        Rtl433Message.Integer(root, "startup"),
        Rtl433Message.Number(root, "rssi"),
        Rtl433Message.Number(root, "snr"));
}
