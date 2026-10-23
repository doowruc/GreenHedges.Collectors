using GreenHedges.HomeServer.Common.Sqlite;
using Microsoft.Data.Sqlite;

namespace GreenHedges.HomeServer.Common.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"collectors-sqlite-{Guid.NewGuid():N}");

    public SqliteDatabaseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private string DbPath => Path.Combine(_dir, "test.db");

    [Fact]
    public async Task Migrations_run_once_each_in_order()
    {
        var v1 = new SqliteDatabase(DbPath, ["CREATE TABLE a (x INTEGER);"]);
        await v1.InitialiseAsync(CancellationToken.None);

        var v2 = new SqliteDatabase(DbPath, ["CREATE TABLE a (x INTEGER);", "INSERT INTO a VALUES (42);"]);
        await v2.InitialiseAsync(CancellationToken.None);
        await v2.InitialiseAsync(CancellationToken.None);

        Assert.Equal(42L, await ScalarAsync(v2, "SELECT SUM(x) FROM a;"));
        Assert.Equal(2L, await ScalarAsync(v2, "PRAGMA user_version;"));
        Assert.Equal("wal", await ScalarAsync(v2, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task A_newer_database_than_the_build_is_refused()
    {
        await new SqliteDatabase(DbPath, ["CREATE TABLE a (x);", "CREATE TABLE b (x);"]).InitialiseAsync(CancellationToken.None);

        var older = new SqliteDatabase(DbPath, ["CREATE TABLE a (x);"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => older.InitialiseAsync(CancellationToken.None));
        Assert.Contains("newer than this build supports", ex.Message);
    }

    [Fact]
    public async Task A_failing_migration_leaves_the_previous_version()
    {
        await new SqliteDatabase(DbPath, ["CREATE TABLE a (x);"]).InitialiseAsync(CancellationToken.None);
        var broken = new SqliteDatabase(DbPath, ["CREATE TABLE a (x);", "CREATE TABLE b (x); NOT SQL;"]);

        await Assert.ThrowsAsync<SqliteException>(() => broken.InitialiseAsync(CancellationToken.None));

        Assert.Equal(1L, await ScalarAsync(broken, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync(broken, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'b';"));
    }

    [Fact]
    public async Task Backup_is_a_self_contained_copy_not_in_wal_mode()
    {
        var database = new SqliteDatabase(DbPath, ["CREATE TABLE a (x INTEGER); INSERT INTO a VALUES (7);"]);
        await database.InitialiseAsync(CancellationToken.None);
        var copyPath = Path.Combine(_dir, "copy.db");

        await database.BackupToAsync(copyPath, CancellationToken.None);

        var copy = new SqliteDatabase(copyPath, []);
        Assert.Equal(7L, await ScalarAsync(copy, "SELECT x FROM a;"));
        Assert.Equal("delete", await ScalarAsync(copy, "PRAGMA journal_mode;"));
        Assert.False(File.Exists(copyPath + "-wal"));
    }

    private static async Task<object?> ScalarAsync(SqliteDatabase database, string sql)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
