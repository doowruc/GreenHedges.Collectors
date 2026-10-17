using System.Globalization;
using System.Text.Json;

namespace GreenHedges.Collectors.Libre.LibreLinkUp;

/// <summary>
/// Lenient readers for LibreLinkUp JSON. Property names mix PascalCase and camelCase
/// (<c>ValueInMgPerDl</c> next to <c>isHigh</c>), so lookups ignore case.
/// </summary>
internal static class JsonHelpers
{
    // FactoryTimestamp is always US-formatted, e.g. "10/16/2026 9:15:00 PM", whatever the account's locale.
    private static readonly string[] TimestampFormats = ["M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss"];

    public static bool TryGet(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static JsonElement? Get(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    public static string? GetString(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static int? GetInt32(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var result) ? result : null;

    public static long? GetInt64(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var result) ? result : null;

    public static double? GetDouble(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    public static bool? GetBoolean(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;

    public static bool TryParseFactoryTimestamp(string? text, out DateTimeOffset utc)
    {
        if (DateTime.TryParseExact(
                text,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utc = new DateTimeOffset(parsed, TimeSpan.Zero);
            return true;
        }

        utc = default;
        return false;
    }

    /// <summary>Returns null if there's no sensor object or it has no serial number.</summary>
    public static SensorInfo? ParseSensor(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } sensor
            || sensor.GetString("sn") is not { Length: > 0 } serial)
        {
            return null;
        }

        return new SensorInfo(
            serial,
            sensor.GetInt64("a") is { } activated and > 0 ? DateTimeOffset.FromUnixTimeSeconds(activated) : null,
            sensor.GetInt32("w"),
            sensor.GetInt32("pt"),
            sensor.GetRawText());
    }

    /// <summary>Returns null if the measurement has no usable timestamp or value.</summary>
    public static GlucoseReading? ParseMeasurement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !TryParseFactoryTimestamp(element.GetString("FactoryTimestamp"), out var timestampUtc)
            || element.GetDouble("ValueInMgPerDl") is not { } valueMgPerDl)
        {
            return null;
        }

        return new GlucoseReading(
            timestampUtc,
            element.GetString("Timestamp"),
            valueMgPerDl,
            element.GetInt32("TrendArrow"),
            element.GetInt32("MeasurementColor"),
            element.GetBoolean("isHigh") ?? false,
            element.GetBoolean("isLow") ?? false,
            element.GetRawText());
    }
}
