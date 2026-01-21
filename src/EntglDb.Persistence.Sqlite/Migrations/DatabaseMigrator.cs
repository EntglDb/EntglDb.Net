using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.Sqlite.Migrations;

/// <summary>
/// Manages database schema migrations for EntglDB SQLite stores.
/// Ensures backward compatibility when new features are added.
/// </summary>
public class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public DatabaseMigrator(string connectionString, ILogger? logger = null)
    {
        _connectionString = connectionString;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Applies all pending migrations to bring the database up to the latest schema version.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Ensure migrations tracking table exists
        await EnsureMigrationsTableAsync(connection);

        // Get current version
        var currentVersion = await GetCurrentVersionAsync(connection);
        _logger.LogInformation("Current database schema version: {Version}", currentVersion);

        // Apply migrations in order
        if (currentVersion < 1)
        {
            await MigrateToVersion1_AddSequenceNumbersAsync(connection);
            await SetVersionAsync(connection, 1);
            _logger.LogInformation("Migrated to schema version 1 (Gap Detection support)");
        }

        // Future migrations go here
        // if (currentVersion < 2) { ... }

        _logger.LogInformation("Database migrations completed successfully");
    }

    /// <summary>
    /// Checks if the database needs migration.
    /// </summary>
    public async Task<bool> NeedsMigrationAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureMigrationsTableAsync(connection);
        var currentVersion = await GetCurrentVersionAsync(connection);
        
        const int latestVersion = 1; // Update when adding new migrations
        return currentVersion < latestVersion;
    }

    /// <summary>
    /// Migration 1: Adds SequenceNumber column to Oplog tables and creates NodeSequenceCounters table.
    /// </summary>
    private async Task MigrateToVersion1_AddSequenceNumbersAsync(SqliteConnection connection)
    {
        _logger.LogInformation("Starting migration to version 1: Adding Gap Detection support");

        // Check if Oplog table exists (legacy mode)
        var hasOplogTable = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Oplog'") > 0;

        if (hasOplogTable)
        {
            // Check if SequenceNumber column already exists
            var hasSequenceNumber = await ColumnExistsAsync(connection, "Oplog", "SequenceNumber");

            if (!hasSequenceNumber)
            {
                _logger.LogInformation("Adding SequenceNumber column to Oplog table");
                await connection.ExecuteAsync(
                    "ALTER TABLE Oplog ADD COLUMN SequenceNumber INTEGER NOT NULL DEFAULT 0");
                
                // Create index
                await connection.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS IDX_Oplog_NodeSeq ON Oplog(HlcNode, SequenceNumber)");
            }
        }

        // Handle per-collection Oplog tables
        var oplogTables = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Oplog_%'");

        foreach (var tableName in oplogTables)
        {
            var hasSequenceNumber = await ColumnExistsAsync(connection, tableName, "SequenceNumber");

            if (!hasSequenceNumber)
            {
                _logger.LogInformation("Adding SequenceNumber column to {Table}", tableName);
                await connection.ExecuteAsync(
                    $"ALTER TABLE {tableName} ADD COLUMN SequenceNumber INTEGER NOT NULL DEFAULT 0");
                
                await connection.ExecuteAsync(
                    $"CREATE INDEX IF NOT EXISTS IDX_{tableName}_NodeSeq ON {tableName}(HlcNode, SequenceNumber)");
            }
        }

        // Create NodeSequenceCounters table if it doesn't exist
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS NodeSequenceCounters (
                NodeId TEXT PRIMARY KEY,
                CurrentSequence INTEGER NOT NULL DEFAULT 0
            )");

        _logger.LogInformation("Migration to version 1 completed");
    }

    private async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        // Simple approach: query for specific column and catch exception if doesn't exist
        try
        {
            var query = $"SELECT {columnName} FROM {tableName} LIMIT 0";
            await connection.ExecuteScalarAsync(query);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureMigrationsTableAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS __EntglDbMigrations (
                Version INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL,
                Description TEXT
            )");
    }

    private async Task<int> GetCurrentVersionAsync(SqliteConnection connection)
    {
        var version = await connection.ExecuteScalarAsync<int?>(
            "SELECT MAX(Version) FROM __EntglDbMigrations");
        return version ?? 0;
    }

    private async Task SetVersionAsync(SqliteConnection connection, int version)
    {
        await connection.ExecuteAsync(
            @"INSERT OR REPLACE INTO __EntglDbMigrations (Version, AppliedAt, Description)
              VALUES (@Version, @AppliedAt, @Description)",
            new
            {
                Version = version,
                AppliedAt = DateTime.UtcNow.ToString("o"),
                Description = GetMigrationDescription(version)
            });
    }

    private string GetMigrationDescription(int version)
    {
        return version switch
        {
            1 => "Added Gap Detection support (SequenceNumber, NodeSequenceCounters)",
            _ => $"Unknown migration version {version}"
        };
    }
}
