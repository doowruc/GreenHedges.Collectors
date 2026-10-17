using Microsoft.Data.Sqlite;

namespace GreenHedges.Collectors.Libre.Storage.Tests;

public sealed class GlucoseArchiveTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 20, 59, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"librecollector-test-{Guid.NewGuid():N}.db");
    private readonly GlucoseArchive _archive;

    public GlucoseArchiveTests() => _archive = new GlucoseArchive(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static ArchivedReading Reading(DateTimeOffset timestamp, ReadingSource source = ReadingSource.Current, double value = 115) =>
        new(timestamp, source, "10/16/2026 9:59:00 PM", value, 3, 1, false, false, T0.AddMinutes(2), """{"ValueInMgPerDl":115}""");

    [Fact]
    public async Task Initialise_creates_schema_and_is_idempotent()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InitialiseAsync(CancellationToken.None);

        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal((long)GlucoseArchive.SchemaVersion, await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Duplicate_readings_are_ignored()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        Assert.Equal(1, await _archive.InsertAsync([Reading(T0)], CancellationToken.None));
        Assert.Equal(0, await _archive.InsertAsync([Reading(T0, value: 999)], CancellationToken.None));
        Assert.Equal(1, await _archive.InsertAsync([Reading(T0), Reading(T0.AddMinutes(1))], CancellationToken.None));

        Assert.Equal(2, await _archive.CountAsync(ReadingSource.Current, CancellationToken.None));
    }

    [Fact]
    public async Task Same_timestamp_from_different_sources_is_kept_separately()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        var inserted = await _archive.InsertAsync(
            [Reading(T0, ReadingSource.Current), Reading(T0, ReadingSource.Graph, value: 112)],
            CancellationToken.None);

        Assert.Equal(2, inserted);
        Assert.Equal(1, await _archive.CountAsync(ReadingSource.Current, CancellationToken.None));
        Assert.Equal(1, await _archive.CountAsync(ReadingSource.Graph, CancellationToken.None));
    }

    [Fact]
    public async Task Latest_timestamp_per_source()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        Assert.Null(await _archive.GetLatestTimestampAsync(ReadingSource.Current, CancellationToken.None));

        await _archive.InsertAsync(
            [Reading(T0.AddMinutes(5)), Reading(T0), Reading(T0.AddHours(1), ReadingSource.Graph)],
            CancellationToken.None);

        Assert.Equal(T0.AddMinutes(5), await _archive.GetLatestTimestampAsync(ReadingSource.Current, CancellationToken.None));
        Assert.Equal(T0.AddHours(1), await _archive.GetLatestTimestampAsync(ReadingSource.Graph, CancellationToken.None));
    }

    [Fact]
    public async Task Backup_is_a_standalone_copy()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync([Reading(T0), Reading(T0, ReadingSource.Graph)], CancellationToken.None);
        var backupPath = _path + ".backup";

        try
        {
            await _archive.BackupToAsync(backupPath, CancellationToken.None);

            Assert.False(File.Exists(backupPath + "-wal"));
            var copy = new GlucoseArchive(backupPath);
            Assert.Equal(1, await copy.CountAsync(ReadingSource.Current, CancellationToken.None));
            Assert.Equal(1, await copy.CountAsync(ReadingSource.Graph, CancellationToken.None));

            await using var connection = new SqliteConnection($"Data Source={backupPath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("delete", await command.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(backupPath);
        }
    }

    [Fact]
    public async Task Stores_all_columns()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [new ArchivedReading(T0, ReadingSource.Graph, null, 54.5, null, 4, false, true, T0.AddMinutes(3), "{}")],
            CancellationToken.None);

        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color, is_high, is_low, received_at_utc, raw_json FROM readings;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("2026-10-16T20:59:00Z", reader.GetString(0));
        Assert.Equal("graph", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal(54.5, reader.GetDouble(3));
        Assert.True(reader.IsDBNull(4));
        Assert.Equal(4, reader.GetInt32(5));
        Assert.Equal(0, reader.GetInt32(6));
        Assert.Equal(1, reader.GetInt32(7));
        Assert.Equal("2026-10-16T21:02:00.000Z", reader.GetString(8));
        Assert.Equal("{}", reader.GetString(9));
    }
}
