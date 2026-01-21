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
using EntglDb.Core.Sync;
using EntglDb.Core.Network;
using System.Text;

namespace EntglDb.Persistence.Sqlite;

/// <summary>
/// SQLite-based implementation of <see cref="IPeerStore"/> with WAL mode enabled.
/// </summary>
public class SqlitePeerStore : IPeerStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqlitePeerStore> _logger;
    private readonly IConflictResolver _conflictResolver;
    private readonly SqlitePersistenceOptions? _options;
    private readonly IPeerNodeConfigurationProvider? _peerNodeConfigurationProvider;
    private readonly HashSet<string> _createdTables = new HashSet<string>();
    private readonly object _tableLock = new object();
    private readonly object _cacheLock = new object();
    private HlcTimestamp _cachedTimestamp = new HlcTimestamp(0, 0, "");

    public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

    /// <summary>
    /// Legacy constructor for backward compatibility - uses connection string directly.
    /// </summary>
    public SqlitePeerStore(string connectionString, ILogger<SqlitePeerStore>? logger = null, IConflictResolver? conflictResolver = null)
    {
        _connectionString = connectionString;
        _logger = logger ?? NullLogger<SqlitePeerStore>.Instance;
        _conflictResolver = conflictResolver ?? new LastWriteWinsConflictResolver();
        _options = null; // Legacy mode - no per-collection tables
        Initialize();
    }

    /// <summary>
    /// New constructor with dynamic database path and per-collection table support.
    /// </summary>
    public SqlitePeerStore(
        IPeerNodeConfigurationProvider configProvider, 
        SqlitePersistenceOptions options,
        ILogger<SqlitePeerStore>? logger = null, 
        IConflictResolver? conflictResolver = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _peerNodeConfigurationProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _logger = logger ?? NullLogger<SqlitePeerStore>.Instance;
        _conflictResolver = conflictResolver ?? new LastWriteWinsConflictResolver();
        _connectionString = BuildConnectionString(configProvider, options).GetAwaiter().GetResult();
        Initialize();
    }

    private async Task<string> BuildConnectionString(IPeerNodeConfigurationProvider configProvider, SqlitePersistenceOptions options)
    {
        var config = await configProvider.GetConfiguration();
        var basePath = options.BasePath ?? SqlitePersistenceOptions.DefaultBasePath;
        
        var filename = options.DatabaseFilenameTemplate.Replace("{NodeId}", config.NodeId);
        var dbPath = Path.Combine(basePath, filename);
        
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created database directory: {Directory}", directory);
        }
        
        _logger.LogInformation("Database path: {DbPath}", dbPath);
        return $"Data Source={dbPath}";
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

            // ========== AUTOMATIC MIGRATION ==========
            var migrator = new Migrations.DatabaseMigrator(_connectionString, _logger);
            var needsMigration = migrator.NeedsMigrationAsync().GetAwaiter().GetResult();
            
            if (needsMigration)
            {
                _logger.LogWarning("Database schema migration required. Applying migrations...");
                migrator.MigrateAsync().GetAwaiter().GetResult();
                _logger.LogInformation("Database migrations applied successfully");
            }
            // ==========================================

            // Only create legacy tables if not using per-collection mode
            if (_options == null || !_options.UsePerCollectionTables)
            {
                // Legacy single-table mode
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
                        HlcNode TEXT NOT NULL,
                        SequenceNumber INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS RemotePeers (
                        NodeId TEXT PRIMARY KEY,
                        Address TEXT NOT NULL,
                        Type INTEGER NOT NULL,
                        OAuth2Json TEXT,
                        IsEnabled INTEGER NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS NodeSequenceCounters (
                        NodeId TEXT PRIMARY KEY,
                        CurrentSequence INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE INDEX IF NOT EXISTS IDX_Oplog_HlcWall ON Oplog(HlcWall);
                    CREATE INDEX IF NOT EXISTS IDX_Oplog_NodeSeq ON Oplog(HlcNode, SequenceNumber);
                ");
                _logger.LogInformation("Initialized with legacy single-table mode");
            }
            else
            {
                // Even in per-collection mode, we need RemotePeers table (it's global, not per-collection)
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS RemotePeers (
                        NodeId TEXT PRIMARY KEY,
                        Address TEXT NOT NULL,
                        Type INTEGER NOT NULL,
                        OAuth2Json TEXT,
                        IsEnabled INTEGER NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS NodeSequenceCounters (
                        NodeId TEXT PRIMARY KEY,
                        CurrentSequence INTEGER NOT NULL DEFAULT 0
                    );
                ");
                _logger.LogInformation("Initialized with per-collection table mode");
            }
            
            // Initialize cached timestamp
            _cachedTimestamp = GetLatestTimestampInternal(connection);
            _logger.LogInformation("Cached HLC Timestamp initialized to: {Timestamp}", _cachedTimestamp);
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

    private string GetDocumentTableName(string collection) =>
        _options?.UsePerCollectionTables == true 
            ? $"Documents_{SanitizeCollectionName(collection)}"
            : "Documents";

    private string GetOplogTableName(string collection) =>
        _options?.UsePerCollectionTables == true 
            ? $"Oplog_{SanitizeCollectionName(collection)}"
            : "Oplog";

    private string SanitizeCollectionName(string collection) =>
        new string(collection.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    private async Task EnsureCollectionTablesAsync(SqliteConnection connection, string collection)
    {
        if (_options?.UsePerCollectionTables != true) return;
        
        // Check if already created (thread-safe)
        lock (_tableLock)
        {
            if (_createdTables.Contains(collection)) return;
        }
        
        var docTable = GetDocumentTableName(collection);
        var oplogTable = GetOplogTableName(collection);
        
        await connection.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {docTable} (
                Key TEXT PRIMARY KEY,
                JsonData TEXT,
                IsDeleted INTEGER NOT NULL,
                HlcWall INTEGER NOT NULL,
                HlcLogic INTEGER NOT NULL,
                HlcNode TEXT NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS {oplogTable} (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Operation INTEGER NOT NULL,
                JsonData TEXT,
                IsDeleted INTEGER NOT NULL,
                HlcWall INTEGER NOT NULL,
                HlcLogic INTEGER NOT NULL,
                HlcNode TEXT NOT NULL,
                SequenceNumber INTEGER NOT NULL DEFAULT 0
            );
            
            CREATE INDEX IF NOT EXISTS IDX_{oplogTable}_HlcWall ON {oplogTable}(HlcWall);
            CREATE INDEX IF NOT EXISTS IDX_{oplogTable}_NodeSeq ON {oplogTable}(HlcNode, SequenceNumber);
        ");
        
        lock (_tableLock)
        {
            _createdTables.Add(collection);
        }
        
        _logger.LogDebug("Created tables for collection: {Collection}", collection);
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
        
        await EnsureCollectionTablesAsync(connection, document.Collection);

        var tableName = GetDocumentTableName(document.Collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        if (usePerCollection)
        {
            // Per-collection mode: no Collection column
            await connection.ExecuteAsync($@"
                INSERT OR REPLACE INTO {tableName} (Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                VALUES (@Key, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                new
                {
                    document.Key,
                    JsonData = document.Content.ValueKind == JsonValueKind.Undefined ? null : document.Content.GetRawText(),
                    document.IsDeleted,
                    HlcWall = document.UpdatedAt.PhysicalTime,
                    HlcLogic = document.UpdatedAt.LogicalCounter,
                    HlcNode = document.UpdatedAt.NodeId
                });
        }
        else
        {
            // Legacy mode: include Collection column
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

        if (document.UpdatedAt.CompareTo(_cachedTimestamp) > 0)
        {
            lock (_cacheLock)
            {
                if (document.UpdatedAt.CompareTo(_cachedTimestamp) > 0)
                {
                    _cachedTimestamp = document.UpdatedAt;
                }
            }
        }
    }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await EnsureCollectionTablesAsync(connection, collection);

        var tableName = GetDocumentTableName(collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        DocumentRow? row;
        if (usePerCollection)
        {
            // Per-collection mode: no Collection filter
            row = await connection.QuerySingleOrDefaultAsync<DocumentRow>($@"
                SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                FROM {tableName}
                WHERE Key = @Key",
                new { Key = key });
        }
        else
        {
            // Legacy mode: filter by Collection
            row = await connection.QuerySingleOrDefaultAsync<DocumentRow>(@"
                SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                FROM Documents
                WHERE Collection = @Collection AND Key = @Key",
                new { Collection = collection, Key = key });
        }

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
        
        await EnsureCollectionTablesAsync(connection, entry.Collection);

        var tableName = GetOplogTableName(entry.Collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        // Get sequence number for this entry (only if enabled)
        long sequenceNumber = entry.SequenceNumber;
        if (sequenceNumber == 0 && _peerNodeConfigurationProvider != null)
        {
            // Entry doesn't have sequence number yet - assign one from local node
            sequenceNumber = await GetAndIncrementSequenceNumberAsync(connection, entry.Timestamp.NodeId);
        }

        if (usePerCollection)
        {
            // Per-collection mode: no Collection column
            await connection.ExecuteAsync($@"
                INSERT INTO {tableName} (Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode, SequenceNumber)
                VALUES (@Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode, @SequenceNumber)",
                new
                {
                    entry.Key,
                    Operation = (int)entry.Operation,
                    JsonData = entry.Payload != null && entry.Payload!.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                    IsDeleted = entry.Operation == OperationType.Delete,
                    HlcWall = entry.Timestamp.PhysicalTime,
                    HlcLogic = entry.Timestamp.LogicalCounter,
                    HlcNode = entry.Timestamp.NodeId,
                    SequenceNumber = sequenceNumber
                });
        }
        else
        {
            // Legacy mode: include Collection column
            await connection.ExecuteAsync(@"
                INSERT INTO Oplog (Collection, Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode, SequenceNumber)
                VALUES (@Collection, @Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode, @SequenceNumber)",
                new
                {
                    entry.Collection,
                    entry.Key,
                    Operation = (int)entry.Operation,
                    JsonData = entry.Payload != null && entry.Payload!.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                    IsDeleted = entry.Operation == OperationType.Delete,
                    HlcWall = entry.Timestamp.PhysicalTime,
                    HlcLogic = entry.Timestamp.LogicalCounter,
                    HlcNode = entry.Timestamp.NodeId,
                    SequenceNumber = sequenceNumber
                });
        }

        // Update cached timestamp
        if (entry.Timestamp.CompareTo(_cachedTimestamp) > 0)
        {
            lock (_cacheLock)
            {
                if (entry.Timestamp.CompareTo(_cachedTimestamp) > 0)
                {
                    _cachedTimestamp = entry.Timestamp;
                }
            }
        }
    }


    /// <summary>
    /// Retrieves all oplog entries with timestamps greater than the specified timestamp.
    /// </summary>
    /// <remarks>
    /// Uses HLC comparison: entries are returned if (Wall > timestamp.Wall) OR (Wall == timestamp.Wall AND Logic > timestamp.Logic).
    /// In per-collection mode, aggregates data from all oplog tables.
    /// </remarks>
    public async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var usePerCollection = _options?.UsePerCollectionTables == true;

        if (!usePerCollection)
        {
            // Legacy mode: single Oplog table
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
        else
        {
            // Per-collection mode: union across all oplog tables
            var collections = GetKnownCollections(connection);
            var allEntries = new List<OplogEntry>();

            foreach (var collection in collections)
            {
                var oplogTable = GetOplogTableName(collection);
                
                var rows = await connection.QueryAsync<OplogRowPerCollection>($@"
                    SELECT Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode
                    FROM {oplogTable}
                    WHERE HlcWall > @HlcWall OR (HlcWall = @HlcWall AND HlcLogic > @HlcLogic)",
                    new { HlcWall = timestamp.PhysicalTime, HlcLogic = timestamp.LogicalCounter });

                allEntries.AddRange(rows.Select(r => new OplogEntry(
                    collection,
                    r.Key ?? "unknown",
                    (OperationType)r.Operation,
                    r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
                    new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? "")
                )));
            }

            return allEntries.OrderBy(e => e.Timestamp.PhysicalTime).ThenBy(e => e.Timestamp.LogicalCounter);
        }
    }


    private List<string> GetKnownCollections(SqliteConnection connection)
    {
        // If we are using per-collection tables, we should query the database for tables that match the pattern,
        // because _createdTables memory cache might be empty after restart.
        if (_options?.UsePerCollectionTables == true)
        {
            var tables = connection.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Oplog_%'");
            
            return tables.Select(t => t.Substring(6)).ToList(); // Remove "Oplog_" prefix
        }

        // Fallback to cache or single table mode logic (though this method is mostly used for per-collection)
        lock (_tableLock)
        {
            return _createdTables.ToList();
        }
    }


    public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
    {
         lock (_cacheLock)
         {
             return Task.FromResult(_cachedTimestamp);
         }
    }

    private class MaxHlcResult
    {
        public long? Wall { get; set; }
        public int? Logic { get; set; }
        public string? HlcNode { get; set; }
    }

    private HlcTimestamp GetLatestTimestampInternal(SqliteConnection connection)
    {
         var usePerCollection = _options?.UsePerCollectionTables == true;

         if (!usePerCollection)
         {
             var row = connection.QuerySingleOrDefault<MaxHlcResult>(@"
                 SELECT MAX(HlcWall) as Wall, MAX(HlcLogic) as Logic, HlcNode 
                 FROM Oplog
                 ORDER BY HlcWall DESC, HlcLogic DESC LIMIT 1");
             
             if (row == null || row.Wall == null) return new HlcTimestamp(0, 0, "");
             return new HlcTimestamp(row.Wall.Value, row.Logic ?? 0, row.HlcNode ?? "");
         }
         else
         {
             var collections = GetKnownCollections(connection);
             HlcTimestamp maxTimestamp = new HlcTimestamp(0, 0, "");

             foreach (var collection in collections)
             {
                 var oplogTable = GetOplogTableName(collection);
                 var row = connection.QuerySingleOrDefault<MaxHlcResult>($@"
                     SELECT MAX(HlcWall) as Wall, MAX(HlcLogic) as Logic, HlcNode 
                     FROM {oplogTable}
                     ORDER BY HlcWall DESC, HlcLogic DESC LIMIT 1");

                 if (row != null && row.Wall != null)
                 {
                     var timestamp = new HlcTimestamp(row.Wall.Value, row.Logic ?? 0, row.HlcNode ?? "");
                     if (timestamp.CompareTo(maxTimestamp) > 0)
                     {
                         maxTimestamp = timestamp;
                     }
                 }
             }
             return maxTimestamp;
         }
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

        var usePerCollection = _options?.UsePerCollectionTables == true;

        try 
        {
            foreach (var entry in oplogEntries)
            {
                await EnsureCollectionTablesAsync(connection, entry.Collection);

                var docTableName = GetDocumentTableName(entry.Collection);
                var oplogTableName = GetOplogTableName(entry.Collection);

                Document? localDoc = null;
                
                if (usePerCollection)
                {
                    var local = await connection.QuerySingleOrDefaultAsync<DocumentRow>($@"
                        SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                        FROM {docTableName}
                        WHERE Key = @Key",
                        new { entry.Key }, transaction);

                    if (local != null)
                    {
                        var localHlc = new HlcTimestamp(local.HlcWall, local.HlcLogic, local.HlcNode);
                        var content = local.JsonData != null 
                            ? JsonSerializer.Deserialize<JsonElement>(local.JsonData) 
                            : default;
                        localDoc = new Document(entry.Collection, local.Key, content, localHlc, local.IsDeleted);
                    }
                }
                else
                {
                    var local = await connection.QuerySingleOrDefaultAsync<DocumentRow>(@"
                        SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                        FROM Documents
                        WHERE Collection = @Collection AND Key = @Key",
                        new { entry.Collection, entry.Key }, transaction);

                    if (local != null)
                    {
                        var localHlc = new HlcTimestamp(local.HlcWall, local.HlcLogic, local.HlcNode);
                        var content = local.JsonData != null 
                            ? JsonSerializer.Deserialize<JsonElement>(local.JsonData) 
                            : default;
                        localDoc = new Document(entry.Collection, local.Key, content, localHlc, local.IsDeleted);
                    }
                }

                var resolution = _conflictResolver.Resolve(localDoc, entry);

                if (resolution.ShouldApply && resolution.MergedDocument != null)
                {
                     var doc = resolution.MergedDocument;
                     
                     if (usePerCollection)
                     {
                         await connection.ExecuteAsync($@"
                             INSERT OR REPLACE INTO {docTableName} (Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                             VALUES (@Key, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                             new
                             {
                                 doc.Key,
                                 JsonData = doc.Content.ValueKind == JsonValueKind.Undefined ? null : doc.Content.GetRawText(),
                                 IsDeleted = doc.IsDeleted ? 1 : 0,
                                 HlcWall = doc.UpdatedAt.PhysicalTime,
                                 HlcLogic = doc.UpdatedAt.LogicalCounter,
                                 HlcNode = doc.UpdatedAt.NodeId
                             }, transaction);
                     }
                     else
                     {
                         await connection.ExecuteAsync(@"
                             INSERT OR REPLACE INTO Documents (Collection, Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                             VALUES (@Collection, @Key, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode)",
                             new
                             {
                                 doc.Collection,
                                 doc.Key,
                                 JsonData = doc.Content.ValueKind == JsonValueKind.Undefined ? null : doc.Content.GetRawText(),
                                 IsDeleted = doc.IsDeleted ? 1 : 0,
                                 HlcWall = doc.UpdatedAt.PhysicalTime,
                                 HlcLogic = doc.UpdatedAt.LogicalCounter,
                                 HlcNode = doc.UpdatedAt.NodeId
                             }, transaction);
                     }
                }
                
                // Get sequence number for this entry (only if enabled)
                long sequenceNumber = entry.SequenceNumber;
                if (sequenceNumber == 0 && _peerNodeConfigurationProvider != null)
                {
                    // Entry doesn't have sequence number yet - assign one from local node
                    sequenceNumber = await GetAndIncrementSequenceNumberAsync(connection, entry.Timestamp.NodeId);
                }

                if (usePerCollection)
                {
                    await connection.ExecuteAsync($@"
                        INSERT INTO {oplogTableName} (Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode, SequenceNumber)
                        VALUES (@Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode, @SequenceNumber)",
                        new
                        {
                            entry.Key,
                            Operation = (int)entry.Operation,
                            JsonData = entry.Payload != null && entry.Payload!.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                            IsDeleted = entry.Operation == OperationType.Delete,
                            HlcWall = entry.Timestamp.PhysicalTime,
                            HlcLogic = entry.Timestamp.LogicalCounter,
                            HlcNode = entry.Timestamp.NodeId,
                            SequenceNumber = sequenceNumber
                        }, transaction);
                }
                else
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO Oplog (Collection, Key, Operation, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode, SequenceNumber)
                        VALUES (@Collection, @Key, @Operation, @JsonData, @IsDeleted, @HlcWall, @HlcLogic, @HlcNode, @SequenceNumber)",
                        new
                        {
                            entry.Collection,
                            entry.Key,
                            Operation = (int)entry.Operation,
                            JsonData = entry.Payload != null && entry.Payload!.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                            IsDeleted = entry.Operation == OperationType.Delete,
                            HlcWall = entry.Timestamp.PhysicalTime,
                            HlcLogic = entry.Timestamp.LogicalCounter,
                            HlcNode = entry.Timestamp.NodeId,
                            SequenceNumber = sequenceNumber
                        }, transaction);
                }
            }
            
            transaction.Commit();
            
            try 
            {
                ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(oplogEntries));

                // Update cache if we have new entries
                var maxEntry = oplogEntries.OrderByDescending(e => e.Timestamp).FirstOrDefault();
                if (maxEntry != null && maxEntry.Timestamp.CompareTo(_cachedTimestamp) > 0)
                {
                    lock (_cacheLock)
                    {
                        if (maxEntry.Timestamp.CompareTo(_cachedTimestamp) > 0)
                        {
                            _cachedTimestamp = maxEntry.Timestamp;
                        }
                    }
                }
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
        
        await EnsureCollectionTablesAsync(connection, collection);

        var translator = new SqlQueryTranslator();
        string whereClause = "1=1";
        var parameters = new DynamicParameters();

        if (queryExpression != null)
        {
            var (w, p) = translator.Translate(queryExpression);
            whereClause = w;
            parameters = p;
        }

        var tableName = GetDocumentTableName(collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        var sqlBuilder = new System.Text.StringBuilder();
        sqlBuilder.Append($@"
                SELECT Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode
                FROM {tableName}
                WHERE IsDeleted = 0");
        
        if (!usePerCollection)
        {
            sqlBuilder.Append(" AND Collection = @Collection");
            parameters.Add("@Collection", collection);
        }
        
        sqlBuilder.Append(" AND (");
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
        
        await EnsureCollectionTablesAsync(connection, collection);

        var tableName = GetDocumentTableName(collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        var sqlBuilder = new StringBuilder();
        sqlBuilder.Append($"SELECT COUNT(*) FROM {tableName} WHERE IsDeleted = 0");

        var dynamicParams = new DynamicParameters();
        
        if (!usePerCollection)
        {
            sqlBuilder.Append(" AND Collection = @Collection");
            dynamicParams.Add("@Collection", collection);
        }

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

        var usePerCollection = _options?.UsePerCollectionTables == true;

        if (!usePerCollection)
        {
            // Legacy mode: query Documents table
            return await connection.QueryAsync<string>(@"
                SELECT DISTINCT Collection 
                FROM Documents 
                ORDER BY Collection");
        }
        else
        {
            // Per-collection mode: return known collections from cache
            return GetKnownCollections(connection);
        }
    }

    public async Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await EnsureCollectionTablesAsync(connection, collection);

        var tableName = GetDocumentTableName(collection);
        var usePerCollection = _options?.UsePerCollectionTables == true;

        // Sanitize names to prevent injection
        var safeColl = new string(collection.Where(char.IsLetterOrDigit).ToArray());
        var safeProp = new string(propertyPath.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.').ToArray());
        var indexName = $"IDX_{safeColl}_{safeProp.Replace(".", "_")}";

        string sql;
        if (usePerCollection)
        {
            // Per-collection mode: simple index without collection filter
            sql = $@"CREATE INDEX IF NOT EXISTS {indexName} 
                         ON {tableName}(json_extract(JsonData, '$.{safeProp}'))";
        }
        else
        {
            // Legacy mode: index with collection filter
            sql = $@"CREATE INDEX IF NOT EXISTS {indexName} 
                         ON Documents(json_extract(JsonData, '$.{safeProp}')) 
                         WHERE Collection = '{collection}'";
        }

        await connection.ExecuteAsync(sql);
        
        _logger.LogInformation("Ensured index {IndexName} on {Collection}.{Property}", indexName, collection, propertyPath);
    }

    // Remote Peer Management
    public async Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = @"
            INSERT OR REPLACE INTO RemotePeers (NodeId, Address, Type, OAuth2Json, IsEnabled)
            VALUES (@NodeId, @Address, @Type, @OAuth2Json, @IsEnabled)";

        await connection.ExecuteAsync(sql, new
        {
            peer.NodeId,
            peer.Address,
            Type = (int)peer.Type,
            peer.OAuth2Json,
            IsEnabled = peer.IsEnabled ? 1 : 0
        });

        _logger.LogInformation("Saved remote peer configuration: {NodeId} ({Type})", peer.NodeId, peer.Type);
    }

    public async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "SELECT NodeId, Address, Type, OAuth2Json, IsEnabled FROM RemotePeers";
        var rows = await connection.QueryAsync<RemotePeerRow>(sql);

        return rows.Select(row => new RemotePeerConfiguration
        {
            NodeId = row.NodeId,
            Address = row.Address,
            Type = (PeerType)row.Type,
            OAuth2Json = row.OAuth2Json,
            IsEnabled = row.IsEnabled == 1
        });
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "DELETE FROM RemotePeers WHERE NodeId = @NodeId";
        var affected = await connection.ExecuteAsync(sql, new { NodeId = nodeId });

        if (affected > 0)
        {
            _logger.LogInformation("Removed remote peer configuration: {NodeId}", nodeId);
        }
        else
        {
            _logger.LogWarning("Attempted to remove non-existent remote peer: {NodeId}", nodeId);
        }
    }

    // Inner classes for Dapper mapping
    private class RemotePeerRow
    {
        public string NodeId { get; set; } = "";
        public string Address { get; set; } = "";
        public int Type { get; set; }
        public string? OAuth2Json { get; set; }
        public int IsEnabled { get; set; }
    }

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

    private class OplogRowPerCollection
    {
        public string? Key { get; set; }
        public int Operation { get; set; }
        public string? JsonData { get; set; }
        public long HlcWall { get; set; }
        public int HlcLogic { get; set; }
        public string? HlcNode { get; set; }
    }

    // ========== Gap Detection Support ==========

    private async Task<long> GetAndIncrementSequenceNumberAsync(SqliteConnection connection, string nodeId)
    {
        // Atomic increment using SQLite's INSERT OR REPLACE with computed values
        await connection.ExecuteAsync(@"
            INSERT INTO NodeSequenceCounters (NodeId, CurrentSequence)
            VALUES (@NodeId, 1)
            ON CONFLICT(NodeId) DO UPDATE SET 
                CurrentSequence = CurrentSequence + 1
            WHERE NodeId = @NodeId",
            new { NodeId = nodeId });

        var result = await connection.QuerySingleAsync<long>(@"
            SELECT CurrentSequence 
            FROM NodeSequenceCounters 
            WHERE NodeId = @NodeId",
            new { NodeId = nodeId });

        return result;
    }

    public async Task<long> GetCurrentSequenceNumberAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (_peerNodeConfigurationProvider == null)
        {
            _logger.LogWarning("PeerNodeConfigurationProvider not available - cannot get sequence number");
            return 0;
        }

        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        var nodeId = config.NodeId;

        var result = await connection.QuerySingleOrDefaultAsync<long?>(@"
            SELECT CurrentSequence 
            FROM NodeSequenceCounters 
            WHERE NodeId = @NodeId",
            new { NodeId = nodeId });

        return result ?? 0;
    }

    public async Task<Dictionary<string, long>> GetPeerSequenceNumbersAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var usePerCollection = _options?.UsePerCollectionTables == true;
        var result = new Dictionary<string, long>();

        if (!usePerCollection)
        {
            // Legacy mode: query single Oplog table
            var rows = await connection.QueryAsync<(string NodeId, long MaxSeq)>(@"
                SELECT HlcNode as NodeId, MAX(SequenceNumber) as MaxSeq
                FROM Oplog
                WHERE SequenceNumber > 0
                GROUP BY HlcNode");

            foreach (var (nodeId, maxSeq) in rows)
            {
                if (!string.IsNullOrEmpty(nodeId))
                {
                    result[nodeId] = maxSeq;
                }
            }
        }
        else
        {
            // Per-collection mode: union across all oplog tables
            var collections = GetKnownCollections(connection);

            foreach (var collection in collections)
            {
                var oplogTable = GetOplogTableName(collection);
                var rows = await connection.QueryAsync<(string NodeId, long MaxSeq)>($@"
                    SELECT HlcNode as NodeId, MAX(SequenceNumber) as MaxSeq
                    FROM {oplogTable}
                    WHERE SequenceNumber > 0
                    GROUP BY HlcNode");

                foreach (var (nodeId, maxSeq) in rows)
                {
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        if (!result.ContainsKey(nodeId) || result[nodeId] < maxSeq)
                        {
                            result[nodeId] = maxSeq;
                        }
                    }
                }
            }
        }

        return result;
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogBySequenceNumbersAsync(
        string nodeId, 
        IEnumerable<long> sequenceNumbers, 
        CancellationToken cancellationToken = default)
    {
        if (!sequenceNumbers.Any())
            return Enumerable.Empty<OplogEntry>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var usePerCollection = _options?.UsePerCollectionTables == true;
        var seqList = string.Join(",", sequenceNumbers);

        if (!usePerCollection)
        {
            // Legacy mode: single Oplog table
            var rows = await connection.QueryAsync<OplogRow>($@"
                SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode
                FROM Oplog
                WHERE HlcNode = @NodeId AND SequenceNumber IN ({seqList})
                ORDER BY SequenceNumber ASC",
                new { NodeId = nodeId });

            return rows.Select(r => new OplogEntry(
                r.Collection ?? "unknown",
                r.Key ?? "unknown",
                (OperationType)r.Operation,
                r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
                new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? ""),
                0 // SequenceNumber will be in row if needed
            ));
        }
        else
        {
            // Per-collection mode: union across all oplog tables
            var collections = GetKnownCollections(connection);
            var allEntries = new List<OplogEntry>();

            foreach (var collection in collections)
            {
                var oplogTable = GetOplogTableName(collection);
                
                var rows = await connection.QueryAsync<OplogRowPerCollection>($@"
                    SELECT Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode
                    FROM {oplogTable}
                    WHERE HlcNode = @NodeId AND SequenceNumber IN ({seqList})",
                    new { NodeId = nodeId });

                allEntries.AddRange(rows.Select(r => new OplogEntry(
                    collection,
                    r.Key ?? "unknown",
                    (OperationType)r.Operation,
                    r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
                    new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? ""),
                    0 // SequenceNumber will be in row if needed
                )));
            }

            return allEntries.OrderBy(e => e.Timestamp.PhysicalTime).ThenBy(e => e.Timestamp.LogicalCounter);
        }
    }
}
