using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Exceptions;
using System.Text;

namespace EntglDb.Persistence.Sqlite
{
    /// <summary>
    /// SQLite-based implementation of <see cref="IPeerStore"/> with WAL mode enabled.
    /// </summary>
    public class SqlitePeerStore : IPeerStore
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlitePeerStore> _logger;

        public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

        public SqlitePeerStore(string connectionString, ILogger<SqlitePeerStore>? logger = null)
        {
            _connectionString = connectionString;
            _logger = logger ?? NullLogger<SqlitePeerStore>.Instance;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Enable WAL mode for better concurrency
                EnsureWalMode(connection);

                // Set busy timeout
                connection.Execute("PRAGMA busy_timeout = 5000");

                _logger.LogInformation("Initializing SQLite database with WAL mode");

                // Create Tables
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS Documents (
                        Collection TEXT NOT NULL,
                        Key TEXT NOT NULL,
                        JsonData TEXT,
                        IsDeleted INTEGER NOT NULL,
                        HlcWall INTEGER NOT NULL,
                        HlcLogic INTEGER NOT NULL,
                        HlcNode TEXT NOT NULL,
                        PRIMARY KEY (Collection, Key)
                    );

                    CREATE TABLE IF NOT EXISTS Oplog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Collection TEXT NOT NULL,
                        Key TEXT NOT NULL,
                        Operation INTEGER NOT NULL,
                        JsonData TEXT,
                        IsDeleted INTEGER NOT NULL,
                        HlcWall INTEGER NOT NULL,
                        HlcLogic INTEGER NOT NULL,
                        HlcNode TEXT NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS IDX_Oplog_HlcWall ON Oplog(HlcWall);
                ");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialize SQLite database");
                throw new PersistenceException("Database initialization failed", ex);
            }
        }

        private void EnsureWalMode(SqliteConnection connection)
        {
            var mode = connection.QuerySingle<string>("PRAGMA journal_mode");
            if (mode?.ToLower() != "wal")
            {
                connection.Execute("PRAGMA journal_mode=WAL");
                _logger.LogInformation("Enabled WAL mode for database");
            }
        }

        /// <summary>
        /// Checks database integrity.
        /// </summary>
        public async Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                
                var result = await connection.QuerySingleAsync<string>("PRAGMA integrity_check");
                var isHealthy = result == "ok";
                
                if (!isHealthy)
                {
                    _logger.LogError("Database corruption detected: {Result}", result);
                    throw new DatabaseCorruptionException($"Database integrity check failed: {result}");
                }
                
                _logger.LogDebug("Database integrity check passed");
                return true;
            }
            catch (Exception ex) when (ex is not DatabaseCorruptionException)
            {
                _logger.LogError(ex, "Failed to check database integrity");
                throw new PersistenceException("Integrity check failed", ex);
            }
        }

        /// <summary>
        /// Creates a backup of the database.
        /// </summary>
        public async Task BackupAsync(string backupPath, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Creating database backup at {Path}", backupPath);
                
                var directory = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var source = new SqliteConnection(_connectionString);
                using var destination = new SqliteConnection($"Data Source={backupPath}");
                
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                
                source.BackupDatabase(destination);
                
                _logger.LogInformation("Database backup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create database backup");
                throw new PersistenceException($"Backup failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves or updates a document in the Documents table.
        /// </summary>
        /// <remarks>
        /// This method updates only the Documents table. The caller is responsible for
        /// maintaining the Oplog separately via <see cref="AppendOplogEntryAsync"/>.
        /// </remarks>
        public async Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(@"
                INSERT OR REPLACE INTO Documents (Collection, Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                VALUES (@Collection, @Key, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                new
                {
                    document.Collection, 
                    document.Key,
                    JsonData = document.Content.ValueKind == JsonValueKind.Undefined ? null : document.Content.GetRawText(),
                    document.IsDeleted,
                    HlcWall = document.UpdatedAt.PhysicalTime,
                    HlcLogic = document.UpdatedAt.LogicalCounter,
                    HlcNode = document.UpdatedAt.NodeId
                });
        }

        public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var row = await connection.QuerySingleOrDefaultAsync<DocumentRow>(@"
                SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                FROM Documents
                WHERE Collection = @Collection AND Key = @Key",
                new { Collection = collection, Key = key });

            if (row == null) return null;

            var hlc = new HlcTimestamp(row.HlcWall, row.HlcLogic, row.HlcNode);
            var content = row.JsonData != null 
                ? JsonSerializer.Deserialize<JsonElement>(row.JsonData) 
                : default;

            return new Document(collection, row.Key, content, hlc, row.IsDeleted); 
        }

        public async Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(@"
                INSERT INTO Oplog (Collection, Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                VALUES (@Collection, @Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                new
                {
                    entry.Collection,
                    entry.Key,
                    Operation = (int)entry.Operation,
                    JsonData = entry.Payload?.GetRawText(),
                    IsDeleted = entry.Operation == OperationType.Delete,
                    HlcWall = entry.Timestamp.PhysicalTime,
                    HlcLogic = entry.Timestamp.LogicalCounter,
                    HlcNode = entry.Timestamp.NodeId
                });
        }

        /// <summary>
        /// Retrieves all oplog entries with timestamps greater than the specified timestamp.
        /// </summary>
        /// <remarks>
        /// Uses HLC comparison: entries are returned if (Wall > timestamp.Wall) OR (Wall == timestamp.Wall AND Logic > timestamp.Logic).
        /// </remarks>
        public async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<OplogRow>(@"
                SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode
                FROM Oplog
                WHERE HlcWall > @HlcWall OR (HlcWall = @HlcWall AND HlcLogic > @HlcLogic)
                ORDER BY HlcWall ASC, HlcLogic ASC",
                new { HlcWall = timestamp.PhysicalTime, HlcLogic = timestamp.LogicalCounter });

            return rows.Select(r => new OplogEntry(
                r.Collection ?? "unknown",
                r.Key ?? "unknown",
                (OperationType)r.Operation,
                r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
                new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? "")
            ));
        }

        public async Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
        {
             using var connection = new SqliteConnection(_connectionString);
             await connection.OpenAsync(cancellationToken);

             var row = await connection.QuerySingleOrDefaultAsync<MaxHlcResult>(@"
                SELECT MAX(HlcWall) as Wall, MAX(HlcLogic) as Logic, HlcNode 
                FROM Oplog
                ORDER BY HlcWall DESC, HlcLogic DESC LIMIT 1");
             
             if (row == null || row.Wall == null) return new HlcTimestamp(0, 0, "");
             
             string nodeId = row.HlcNode ?? "";
             return new HlcTimestamp(row.Wall.Value, row.Logic ?? 0, nodeId); 
        }

        private class MaxHlcResult
        {
            public long? Wall { get; set; }
            public int? Logic { get; set; }
            public string? HlcNode { get; set; }
        }

        /// <summary>
        /// Applies a batch of oplog entries using Last-Write-Wins conflict resolution.
        /// </summary>
        /// <remarks>
        /// For each entry, compares the incoming HLC timestamp with the local document's timestamp.
        /// Only applies changes if the incoming timestamp is newer. All entries are appended to the Oplog.
        /// </remarks>
        public async Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            try 
            {
                foreach (var entry in oplogEntries)
                {
                    var local = await connection.QuerySingleOrDefaultAsync<DocumentRow>(@"
                        SELECT HlcWall, HlcLogic, HlcNode
                        FROM Documents
                        WHERE Collection = @Collection AND Key = @Key",
                        new { entry.Collection, entry.Key }, transaction);

                    bool shouldApply = false;
                    if (local == null)
                    {
                        shouldApply = true;
                    }
                    else
                    {
                        var localHlc = new HlcTimestamp(local.HlcWall, local.HlcLogic, local.HlcNode);
                        if (entry.Timestamp.CompareTo(localHlc) > 0)
                        {
                            shouldApply = true;
                        }
                    }

                    if (shouldApply)
                    {
                         await connection.ExecuteAsync(@"
                            INSERT OR REPLACE INTO Documents (Collection, Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                            VALUES (@Collection, @Key, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                            new
                            {
                                entry.Collection,
                                entry.Key,
                                JsonData = entry.Payload?.GetRawText(),
                                IsDeleted = entry.Operation == OperationType.Delete ? 1 : 0,
                                HlcWall = entry.Timestamp.PhysicalTime,
                                HlcLogic = entry.Timestamp.LogicalCounter,
                                HlcNode = entry.Timestamp.NodeId
                            }, transaction);
                    }
                    
                    await connection.ExecuteAsync(@"
                        INSERT INTO Oplog (Collection, Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                        VALUES (@Collection, @Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                        new
                        {
                            entry.Collection,
                            entry.Key,
                            Operation = (int)entry.Operation,
                            JsonData = entry.Payload?.GetRawText(),
                            IsDeleted = entry.Operation == OperationType.Delete,
                            HlcWall = entry.Timestamp.PhysicalTime,
                            HlcLogic = entry.Timestamp.LogicalCounter,
                            HlcNode = entry.Timestamp.NodeId
                        }, transaction);
                }
                
                transaction.Commit();
                
                try 
                {
                    ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(oplogEntries));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling ChangesApplied event");
                }
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var translator = new SqlQueryTranslator();
            // Handle null queryExpression (meaning "All")
            string whereClause = "1=1";
            var parameters = new DynamicParameters();

            if (queryExpression != null)
            {
                var (w, p) = translator.Translate(queryExpression);
                whereClause = w;
                parameters = p;
            }

            parameters.Add("@Collection", collection);

            var sqlBuilder = new System.Text.StringBuilder();
            sqlBuilder.Append(@"
                SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                FROM Documents
                WHERE Collection = @Collection AND IsDeleted = 0 AND (");
            sqlBuilder.Append(whereClause);
            sqlBuilder.Append(")");

            if (!string.IsNullOrEmpty(orderBy))
            {
                if (orderBy.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_'))
                {
                    string sortField = $"json_extract(JsonData, '$.{orderBy}')";
                    sqlBuilder.Append($" ORDER BY {sortField} {(ascending ? "ASC" : "DESC")}");
                }
            }
            else
            {
                 sqlBuilder.Append(" ORDER BY Key ASC");
            }

            // Paging
            if (take.HasValue)
            {
                sqlBuilder.Append(" LIMIT @Take");
                parameters.Add("@Take", take.Value);
            }

            if (skip.HasValue)
            {
                sqlBuilder.Append(" OFFSET @Skip");
                parameters.Add("@Skip", skip.Value);
            }

            var rows = await connection.QueryAsync<DocumentRow>(sqlBuilder.ToString(), parameters);
            
            return rows.Select(r => {
                 var hlc = new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode);
                 var content = r.JsonData != null 
                    ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) 
                    : default;
                 return new Document(collection, r.Key, content, hlc, r.IsDeleted);
            });
        }

        public async Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append("SELECT COUNT(*) FROM Documents WHERE Collection = @Collection AND IsDeleted = 0");

            var dynamicParams = new DynamicParameters();
            dynamicParams.Add("@Collection", collection);

            if (queryExpression != null)
            {
                var translator = new SqlQueryTranslator();
                var (whereClause, queryParams) = translator.Translate(queryExpression);
                if (!string.IsNullOrEmpty(whereClause))
                {
                    sqlBuilder.Append($" AND ({whereClause})");
                    dynamicParams.AddDynamicParams(queryParams);
                }
            }

            return await connection.ExecuteScalarAsync<int>(sqlBuilder.ToString(), dynamicParams);
        }

        public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            return await connection.QueryAsync<string>(@"
                SELECT DISTINCT Collection 
                FROM Documents 
                ORDER BY Collection");
        }

        public async Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Sanitize names to prevent injection (though internal use makes this less risky)
            var safeColl = new string(collection.Where(char.IsLetterOrDigit).ToArray());
            var safeProp = new string(propertyPath.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.').ToArray());
            var indexName = $"IDX_{safeColl}_{safeProp.Replace(".", "_")}";

            // SQLite JSON index syntax
            var sql = $@"CREATE INDEX IF NOT EXISTS {indexName} 
                         ON Documents(json_extract(JsonData, '$.{safeProp}')) 
                         WHERE Collection = '{collection}'";

            await connection.ExecuteAsync(sql);
            
            _logger.LogInformation("Ensured index {IndexName} on {Collection}.{Property}", indexName, collection, propertyPath);
        }

        // Inner classes for Dapper mapping
        private class DocumentRow
        {
            public string Key { get; set; } = "";
            public string? JsonData { get; set; }
            public bool IsDeleted { get; set; }
            public long HlcWall { get; set; }
            public int HlcLogic { get; set; }
            public string HlcNode { get; set; } = "";
        }

        private class OplogRow
        {
            public string? Collection { get; set; }
            public string? Key { get; set; }
            public int Operation { get; set; }
            public string? JsonData { get; set; }
            public long HlcWall { get; set; }
            public int HlcLogic { get; set; }
            public string? HlcNode { get; set; }
        }
    }
}
