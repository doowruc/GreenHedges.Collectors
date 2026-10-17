namespace GreenHedges.Collectors.Weather.Rtl433.Tests;

public sealed class Rtl433MessageTests
{
    // Shaped like the reception test's output (rtl_433 -R 172 -M time:unix:usec -M level -M protocol -F json),
    // with a made-up id. A Bresser 5-in-1 alternates these two messages every 12 s.
    private const string TemperatureLine =
        """{"time" : "1792234567.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "battery_ok" : 0, "temperature_C" : 11.6, "humidity" : 87, "sensor_type" : 1, "wind_max_m_s" : 1.2, "wind_avg_m_s" : 0.9, "wind_dir_deg" : 112, "uvi" : 0.000, "startup" : 1, "flags" : 0, "mic" : "CRC", "mod" : "FSK", "freq1" : 868.274, "freq2" : 868.336, "rssi" : -1.523, "snr" : 22.104, "noise" : -23.627}""";

    private const string RainLine =
        """{"time" : "1792234579.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "sensor_type" : 1, "wind_max_m_s" : 2.1, "wind_avg_m_s" : 1.4, "wind_dir_deg" : 45, "rain_mm" : 998.4, "startup" : 1, "flags" : 1, "mic" : "CRC", "mod" : "FSK", "freq1" : 868.270, "freq2" : 868.332, "rssi" : -1.610, "snr" : 21.877, "noise" : -23.487}""";

    [Fact]
    public void Temperature_message_is_identified_and_typed()
    {
        var message = Rtl433Message.Parse(TemperatureLine);

        Assert.True(message.IsValidJson);
        Assert.Equal(TemperatureLine, message.Raw);
        Assert.Equal("Bresser-6in1", message.Model);
        Assert.Equal(172, message.Protocol);
        Assert.Equal("12345678", message.DeviceId);
        Assert.Equal("0", message.Channel);
        Assert.Equal("1792234567.123456", message.Time);

        var weather = Assert.IsType<WeatherFields>(message.Weather);
        Assert.Equal(11.6, weather.TemperatureC);
        Assert.Equal(87, weather.Humidity);
        Assert.Equal(0.9, weather.WindAvgMs);
        Assert.Equal(1.2, weather.WindMaxMs);
        Assert.Equal(112, weather.WindDirDeg);
        Assert.Null(weather.RainMm);
        Assert.Equal(0, weather.BatteryOk);
        Assert.Equal(0, weather.Flags);
        Assert.Equal(1, weather.Startup);
        Assert.Equal(-1.523, weather.Rssi);
        Assert.Equal(22.104, weather.Snr);
    }

    [Fact]
    public void Rain_message_has_rain_and_wind_but_no_temperature_or_battery()
    {
        var weather = Rtl433Message.Parse(RainLine).Weather!;

        Assert.Equal(998.4, weather.RainMm);
        Assert.Equal(1.4, weather.WindAvgMs);
        Assert.Equal(1, weather.Flags);
        Assert.Null(weather.TemperatureC);
        Assert.Null(weather.Humidity);
        Assert.Null(weather.BatteryOk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"model" : "Bresser-6in1", "id" : 1""")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    public void Anything_that_is_not_a_json_object_is_kept_but_flagged(string line)
    {
        var message = Rtl433Message.Parse(line);

        Assert.False(message.IsValidJson);
        Assert.Equal(line, message.Raw);
        Assert.Null(message.Model);
        Assert.Null(message.DeviceId);
        Assert.Null(message.Weather);
    }

    [Fact]
    public void Text_ids_and_channels_are_kept_as_written()
    {
        var message = Rtl433Message.Parse("""{"model" : "Acurite-Tower", "id" : "0x1a2b", "channel" : "A", "time" : 1792234567}""");

        Assert.Equal("0x1a2b", message.DeviceId);
        Assert.Equal("A", message.Channel);
        Assert.Equal("1792234567", message.Time);
        Assert.Null(message.Protocol);
    }

    [Fact]
    public void Numbers_written_as_strings_are_read()
    {
        var weather = Rtl433Message.Parse("""{"model" : "X", "temperature_C" : "12.5", "humidity" : "n/a"}""").Weather!;

        Assert.Equal(12.5, weather.TemperatureC);
        Assert.Null(weather.Humidity);
    }

    [Fact]
    public void A_device_without_weather_fields_has_them_all_null()
    {
        var weather = Rtl433Message.Parse("""{"model" : "Doorbell", "id" : 7}""").Weather!;

        Assert.Equal(new WeatherFields(null, null, null, null, null, null, null, null, null, null, null), weather);
    }
}
