using System.Globalization;
using System.Text.Json;

namespace GreenHedges.HomeServer.Collectors.Weather.Rtl433;

/// <summary>
/// One line of <c>rtl_433 -F json</c> output: the line exactly as written, plus the fields that identify
/// the device. Nothing is thrown away: a line that isn't a JSON object is kept with <see cref="IsValidJson"/>
/// false, so it can still be archived.
/// </summary>
/// <param name="Raw">The line as rtl_433 wrote it (without the line ending).</param>
/// <param name="IsValidJson">Whether the line is a JSON object. If false, every other field is null.</param>
/// <param name="Model">Decoder model, e.g. <c>Bresser-6in1</c>.</param>
/// <param name="Protocol">rtl_433 protocol number (needs <c>-M protocol</c>), e.g. 172.</param>
/// <param name="DeviceId">The device's <c>id</c> as text: models differ (numbers, hex strings).</param>
/// <param name="Channel">The device's <c>channel</c> as text: models differ (numbers, letters).</param>
/// <param name="Time">rtl_433's own <c>time</c> as written (with <c>-M time:unix:usec</c>, epoch seconds).</param>
public sealed record Rtl433Message(
    string Raw,
    bool IsValidJson,
    string? Model,
    int? Protocol,
    string? DeviceId,
    string? Channel,
    string? Time)
{
    /// <summary>Weather fields, if the line had any (null for an invalid line).</summary>
    public WeatherFields? Weather { get; init; }

    public static Rtl433Message Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid(line);
            }

            return new Rtl433Message(
                line,
                IsValidJson: true,
                Model: Text(root, "model"),
                Protocol: Integer(root, "protocol"),
                DeviceId: Text(root, "id"),
                Channel: Text(root, "channel"),
                Time: Text(root, "time"))
            {
                Weather = WeatherFields.From(root),
            };
        }
        catch (JsonException)
        {
            return Invalid(line);
        }
    }

    private static Rtl433Message Invalid(string line) => new(line, false, null, null, null, null, null);

    /// <summary>A string as is, or a number as written (so <c>318836632</c> stays exactly that).</summary>
    internal static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    internal static int? Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>A number, or a string holding one (some decoders write numbers as strings).</summary>
    internal static double? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }
}
