using Microsoft.Data.Sqlite;

namespace GreenHedges.Collectors.Common.Sqlite;

/// <summary>
/// The SQLite plumbing every collector's archive shares: connections, WAL mode, schema migrations
/// tracked in <c>PRAGMA user_version</c>, and consistent integrity-checked backup copies.
/// </summary>
/// <param name="databasePath">The database file; created if missing.</param>
/// <param name="migrations">
/// Append-only. Each entry upgrades the schema by one version, in its own transaction, so the schema
/// version is the number of migrations.
/// </param>
public sealed class SqliteDatabase(string databasePath, IReadOnlyList<string> migrations)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    public int SchemaVersion => migrations.Count;

    /// <summary>Creates the database if needed, switches it to WAL and applies any pending migrations.</summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);

        await using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            await wal.ExecuteNonQueryAsync(cancellationToken);
        }

        var version = await GetUserVersionAsync(connection, cancellationToken);
        if (version > migrations.Count)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than this build supports ({migrations.Count}).");
        }

        for (var next = (int)version; next < migrations.Count; next++)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"{migrations[next]}\nPRAGMA user_version = {next + 1};";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Writes a consistent copy of the database to <paramref name="destinationPath"/> (replacing any file
    /// there) using SQLite's online backup, safe while the collector is writing. The copy is a single
    /// self-contained file, and it's integrity-checked before returning.
    /// </summary>
    public async Task BackupToAsync(string destinationPath, CancellationToken cancellationToken)
    {
        File.Delete(destinationPath);

        await using var source = await OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);

        await using var command = destination.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = DELETE;";
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (result != "ok")
        {
            throw new InvalidOperationException($"Backup integrity check failed: {result}");
        }
    }

    private static async Task<long> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
