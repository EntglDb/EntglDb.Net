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
    private readonly HashSet<string> _createdTables = new HashSet<string>();
    private readonly object _tableLock = new object();
    private readonly object _cacheLock = new object();

    // Per-node cache: tracks latest timestamp and hash for each node
    private readonly Dictionary<string, NodeCacheEntry> _nodeCache = new Dictionary<string, NodeCacheEntry>(StringComparer.Ordinal);

    private class NodeCacheEntry
    {
        public HlcTimestamp Timestamp { get; set; }
        public string Hash { get; set; } = "";
    }

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

            // Unified Oplog and RemotePeers (Always created)
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Oplog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Collection TEXT NOT NULL,
                    Key TEXT NOT NULL,
                    Operation INTEGER NOT NULL,
                    JsonData TEXT,
                    HlcWall INTEGER NOT NULL,
                    HlcLogic INTEGER NOT NULL,
                    HlcNode TEXT NOT NULL,
                    Hash TEXT,
                    PreviousHash TEXT
                );
                
                CREATE TABLE IF NOT EXISTS RemotePeers (
                    NodeId TEXT PRIMARY KEY,
                    Address TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    OAuth2Json TEXT,
                    IsEnabled INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IDX_Oplog_HlcWall ON Oplog(HlcWall);
                CREATE INDEX IF NOT EXISTS IDX_Oplog_Hash ON Oplog(Hash);
            
                CREATE TABLE IF NOT EXISTS SnapshotMetadata (
                    NodeId TEXT PRIMARY KEY,
                    HlcWall INTEGER NOT NULL,
                    HlcLogic INTEGER NOT NULL,
                    Hash TEXT
                );
            ");

            // Documents Table (Legacy Mode Only)
            if (_options == null || !_options.UsePerCollectionTables)
            {
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
                ");
                _logger.LogInformation("Initialized with legacy single-table mode");
            }
            else
            {
                _logger.LogInformation("Initialized with per-collection table mode");
            }

            // Initialize node cache from database
            InitializeNodeCache(connection);
            _logger.LogInformation("Node cache initialized with {Count} nodes", _nodeCache.Count);
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

    private void InitializeNodeCache(SqliteConnection connection)
    {
        lock (_cacheLock)
        {
            _nodeCache.Clear();

            // 1. Load from SnapshotMetadata (Base State)
            var snapshots = connection.Query<(string NodeId, long HlcWall, int HlcLogic, string Hash)>("SELECT NodeId, HlcWall, HlcLogic, Hash FROM SnapshotMetadata");
            foreach (var s in snapshots)
            {
                _nodeCache[s.NodeId] = new NodeCacheEntry
                {
                    Timestamp = new HlcTimestamp(s.HlcWall, s.HlcLogic, s.NodeId),
                    Hash = s.Hash ?? ""
                };
            }

            // 2. Load from Oplog (Latest State - Overrides Snapshot if newer)
            var rows = connection.Query<(string NodeId, long HlcWall, int HlcLogic, string Hash)>(@"
                SELECT HlcNode as NodeId, HlcWall, HlcLogic, Hash
                FROM Oplog o1
                WHERE (HlcWall, HlcLogic) = (
                    SELECT MAX(HlcWall), MAX(HlcLogic)
                    FROM Oplog o2
                    WHERE o2.HlcNode = o1.HlcNode
                )
                GROUP BY HlcNode");

            foreach (var row in rows)
            {
                var timestamp = new HlcTimestamp(row.HlcWall, row.HlcLogic, row.NodeId);

                // Only update if newer (though Oplog usually contains newer data than snapshot)
                if (!_nodeCache.TryGetValue(row.NodeId, out var existing) || timestamp.CompareTo(existing.Timestamp) > 0)
                {
                    _nodeCache[row.NodeId] = new NodeCacheEntry
                    {
                        Timestamp = timestamp,
                        Hash = row.Hash ?? ""
                    };
                }
            }
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
                HlcWall INTEGER NOT NULL,
                HlcLogic INTEGER NOT NULL,
                HlcNode TEXT NOT NULL
            );
            
            CREATE INDEX IF NOT EXISTS IDX_{oplogTable}_HlcWall ON {oplogTable}(HlcWall);
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
            // Create legacy table if needed (since we removed it from Initialize)
            await connection.ExecuteAsync(@"
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
            ");

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

        // Note: This method only saves the document, not the oplog entry
        // The hash will be updated when AppendOplogEntryAsync is called
        lock (_cacheLock)
        {
            var nodeId = document.UpdatedAt.NodeId;
            if (!_nodeCache.TryGetValue(nodeId, out var entry) || document.UpdatedAt.CompareTo(entry.Timestamp) > 0)
            {
                // Update timestamp but keep existing hash (will be updated by AppendOplogEntryAsync)
                var existingHash = _nodeCache.TryGetValue(nodeId, out var e) ? e.Hash : "";
                _nodeCache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = document.UpdatedAt,
                    Hash = existingHash
                };
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

        return new Document(collection, row.Key, content, hlc, row.IsDeleted != 0);
    }

    public async Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Unified Oplog Table: Always use single Oplog table regardless of per-collection setting
        await connection.ExecuteAsync(@"
            INSERT INTO Oplog (Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash)
            VALUES (@Collection, @Key, @Operation, @JsonData, @HlcWall, @HlcLogic, @HlcNode, @Hash, @PreviousHash)",
            new
            {
                entry.Collection,
                entry.Key,
                Operation = (int)entry.Operation,
                JsonData = entry.Payload.HasValue && entry.Payload.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                HlcWall = entry.Timestamp.PhysicalTime,
                HlcLogic = entry.Timestamp.LogicalCounter,
                HlcNode = entry.Timestamp.NodeId,
                entry.Hash,
                entry.PreviousHash
            });

        // Update node cache with both timestamp and hash
        lock (_cacheLock)
        {
            var nodeId = entry.Timestamp.NodeId;
            if (!_nodeCache.TryGetValue(nodeId, out var existing) || entry.Timestamp.CompareTo(existing.Timestamp) > 0)
            {
                _nodeCache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = entry.Timestamp,
                    Hash = entry.Hash ?? ""
                };
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

        // Unified query: We now only use the single Oplog table for sync
        var rows = await connection.QueryAsync<OplogRow>(@"
            SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash
            FROM Oplog
            WHERE (HlcWall > @Wall) OR (HlcWall = @Wall AND HlcLogic > @Logic)
            ORDER BY HlcWall ASC, HlcLogic ASC",
            new { Wall = timestamp.PhysicalTime, Logic = timestamp.LogicalCounter });

        return rows.Select(r => new OplogEntry(
            r.Collection ?? "",
            r.Key ?? "",
            (OperationType)r.Operation,
            r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
            new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? ""),
            r.PreviousHash ?? "",
            r.Hash
        ));
    }

    public async Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // Try cache first
        lock (_cacheLock)
        {
            if (_nodeCache.TryGetValue(nodeId, out var entry))
            {
                return entry.Hash;
            }
        }

        // Cache miss - query database (Oplog first, then SnapshotMetadata)
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var hash = await connection.QuerySingleOrDefaultAsync<string>(@"
            SELECT Hash 
            FROM Oplog 
            WHERE HlcNode = @NodeId 
            ORDER BY HlcWall DESC, HlcLogic DESC 
            LIMIT 1", new { NodeId = nodeId });

        if (hash == null)
        {
            // Fallback to snapshot
            hash = await connection.QuerySingleOrDefaultAsync<string>(@"
                SELECT Hash 
                FROM SnapshotMetadata 
                WHERE NodeId = @NodeId", new { NodeId = nodeId });
        }

        // Update cache if found
        if (hash != null)
        {
            // Try to get timestamp from Oplog
            var row = await connection.QuerySingleOrDefaultAsync<(long Wall, int Logic)?>(@"
                SELECT HlcWall as Wall, HlcLogic as Logic
                FROM Oplog 
                WHERE Hash = @Hash", new { Hash = hash });

            if (row == null)
            {
                // Try to get timestamp from Snapshot
                row = await connection.QuerySingleOrDefaultAsync<(long Wall, int Logic)?>(@"
                SELECT HlcWall as Wall, HlcLogic as Logic
                FROM SnapshotMetadata 
                WHERE Hash = @Hash", new { Hash = hash });
            }

            if (row.HasValue)
            {
                lock (_cacheLock)
                {
                    _nodeCache[nodeId] = new NodeCacheEntry
                    {
                        Timestamp = new HlcTimestamp(row.Value.Wall, row.Value.Logic, nodeId),
                        Hash = hash
                    };
                }
            }
        }

        return hash;
    }

    public async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<OplogRow>(@"
            SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, PreviousHash, Hash 
            FROM Oplog 
            WHERE Hash = @Hash", new { Hash = hash });

        if (row == null) return null;

        return new OplogEntry(
            row.Collection ?? "unknown",
            row.Key ?? "unknown",
            (OperationType)row.Operation,
            row.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(row.JsonData) : (JsonElement?)null,
            new HlcTimestamp(row.HlcWall, row.HlcLogic, row.HlcNode ?? ""),
            row.PreviousHash ?? ""
        );
    }

    public async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // 1. Fetch range bounds
        var startRow = await connection.QuerySingleOrDefaultAsync<OplogRow>(
            "SELECT HlcWall, HlcLogic, HlcNode FROM Oplog WHERE Hash = @Hash", new { Hash = startHash });
        var endRow = await connection.QuerySingleOrDefaultAsync<OplogRow>(
            "SELECT HlcWall, HlcLogic, HlcNode FROM Oplog WHERE Hash = @Hash", new { Hash = endHash });

        if (startRow == null || endRow == null) return Enumerable.Empty<OplogEntry>();
        if (startRow.HlcNode != endRow.HlcNode) return Enumerable.Empty<OplogEntry>(); // Must be same chain

        // 2. Fetch range (Start < Entry <= End)
        var rows = await connection.QueryAsync<OplogRow>(@"
            SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, PreviousHash, Hash 
            FROM Oplog
            WHERE HlcNode = @NodeId
              AND ( (HlcWall > @StartWall) OR (HlcWall = @StartWall AND HlcLogic > @StartLogic) )
              AND ( (HlcWall < @EndWall) OR (HlcWall = @EndWall AND HlcLogic <= @EndLogic) )
            ORDER BY HlcWall ASC, HlcLogic ASC",
            new
            {
                NodeId = startRow.HlcNode,
                StartWall = startRow.HlcWall,
                StartLogic = startRow.HlcLogic,
                EndWall = endRow.HlcWall,
                EndLogic = endRow.HlcLogic
            });

        return rows.Select(r => new OplogEntry(
            r.Collection ?? "unknown",
            r.Key ?? "unknown",
            (OperationType)r.Operation,
            r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
            new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? ""),
            r.PreviousHash ?? ""
        ));
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
        // Return the maximum timestamp from cache
        lock (_cacheLock)
        {
            if (_nodeCache.Count == 0)
            {
                return Task.FromResult(new HlcTimestamp(0, 0, ""));
            }

            var maxTimestamp = _nodeCache.Values
                .Select(e => e.Timestamp)
                .OrderByDescending(t => t)
                .First();

            return Task.FromResult(maxTimestamp);
        }
    }

    public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
    {
        // Return cached vector clock
        lock (_cacheLock)
        {
            var vectorClock = new VectorClock();
            foreach (var kvp in _nodeCache)
            {
                vectorClock.SetTimestamp(kvp.Key, kvp.Value.Timestamp);
            }
            return Task.FromResult(vectorClock);
        }
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<OplogRow>(@"
            SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, PreviousHash, Hash 
            FROM Oplog
            WHERE HlcNode = @NodeId
              AND ( (HlcWall > @Wall) OR (HlcWall = @Wall AND HlcLogic > @Logic) )
            ORDER BY HlcWall ASC, HlcLogic ASC",
            new { NodeId = nodeId, Wall = since.PhysicalTime, Logic = since.LogicalCounter });

        return rows.Select(r => new OplogEntry(
            r.Collection ?? "unknown",
            r.Key ?? "unknown",
            (OperationType)r.Operation,
            r.JsonData != null ? JsonSerializer.Deserialize<JsonElement>(r.JsonData) : (JsonElement?)null,
            new HlcTimestamp(r.HlcWall, r.HlcLogic, r.HlcNode ?? ""),
            r.PreviousHash ?? ""
        ));
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

        var usePerCollection = _options?.UsePerCollectionTables == true;

        try
        {
            foreach (var entry in oplogEntries)
            {
                await EnsureCollectionTablesAsync(connection, entry.Collection);

                var docTableName = GetDocumentTableName(entry.Collection);

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
                        localDoc = new Document(entry.Collection, local.Key, content, localHlc, local.IsDeleted != 0);
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
                        localDoc = new Document(entry.Collection, local.Key, content, localHlc, local.IsDeleted != 0);
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

                // Unified Oplog Table: Always use single Oplog table regardless of per-collection setting
                await connection.ExecuteAsync(@"
                    INSERT INTO Oplog (Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash)
                    VALUES (@Collection, @Key, @Operation, @JsonData, @HlcWall, @HlcLogic, @HlcNode, @Hash, @PreviousHash)",
                    new
                    {
                        entry.Collection,
                        entry.Key,
                        Operation = (int)entry.Operation,
                        JsonData = entry.Payload.HasValue && entry.Payload.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                        HlcWall = entry.Timestamp.PhysicalTime,
                        HlcLogic = entry.Timestamp.LogicalCounter,
                        HlcNode = entry.Timestamp.NodeId,
                        entry.Hash,
                        entry.PreviousHash
                    }, transaction);
            }

            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26) // SQLITE_CORRUPT or SQLITE_NOTADB
        {
             _logger.LogCritical(ex, "Database corruption detected during ApplyBatchAsync!");
             try { transaction.Rollback(); } catch { }
             throw new CorruptDatabaseException("SQLite database is corrupt", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply batch");
            try { transaction.Rollback(); } catch { }
            throw;
        }

        // Invalidate node cache so vector clocks and last-entry hashes will be recomputed
        _nodeCache?.Clear();
        // Notify changes
        ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(oplogEntries));
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM RemotePeers WHERE NodeId = @NodeId", new { NodeId = nodeId });
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

    public async Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default)
    {
        // Delegate to QueryDocumentsAsync to ensure identical filtering semantics,
        // then count the resulting documents.
        var documents = await QueryDocumentsAsync(collection, queryExpression, null, null, null, true, cancellationToken);

        if (documents is ICollection<Document> collectionDocuments)
        {
            return collectionDocuments.Count;
        }

        return documents.Count();
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
                         WHERE Collection = '{safeColl}'";
        }

        await connection.ExecuteAsync(sql);
        
        _logger.LogInformation("Ensured index {IndexName} on {Collection}.{Property}", indexName, collection, propertyPath);
    }

    public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (_options?.UsePerCollectionTables == true)
        {
            // In per-collection mode, rely on known collection tables.
            return GetKnownCollections(connection);
        }

        // In legacy single-table mode, enumerate distinct collection names from the Documents table
        const string sql = "SELECT DISTINCT Collection FROM Documents";
        var collections = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)
        ).ConfigureAwait(false);

        return collections;
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
             return new Document(collection, r.Key, content, hlc, r.IsDeleted != 0);
        });
    }

    // --- Snapshotting Implementation ---

    public async Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Identify entries that will become the "boundary" (Max <= Cutoff)
            var boundaries = await connection.QueryAsync<(string NodeId, long HlcWall, int HlcLogic, string Hash)>(@"
                SELECT HlcNode as NodeId, HlcWall, HlcLogic, Hash
                FROM Oplog o1
                WHERE (HlcWall, HlcLogic) = (
                    SELECT MAX(HlcWall), MAX(HlcLogic)
                    FROM Oplog o2
                    WHERE o2.HlcNode = o1.HlcNode
                      AND (o2.HlcWall < @Wall OR (o2.HlcWall = @Wall AND o2.HlcLogic <= @Logic))
                )
                GROUP BY HlcNode",
                new { Wall = cutoff.PhysicalTime, Logic = cutoff.LogicalCounter }, transaction);

            // 2. Upsert SnapshotMetadata
            foreach (var b in boundaries)
            {
                await connection.ExecuteAsync(@"
                    INSERT OR REPLACE INTO SnapshotMetadata (NodeId, HlcWall, HlcLogic, Hash)
                    VALUES (@NodeId, @HlcWall, @HlcLogic, @Hash)",
                    b, transaction);
            }

            // 3. Delete old entries
            await connection.ExecuteAsync(@"
                DELETE FROM Oplog
                WHERE HlcWall < @Wall OR (HlcWall = @Wall AND HlcLogic <= @Logic)",
                new { Wall = cutoff.PhysicalTime, Logic = cutoff.LogicalCounter }, transaction);

            transaction.Commit();

            _logger.LogInformation("Pruned oplog entries older than {Cutoff}. Updated metadata for {Count} nodes.", cutoff, boundaries.Count());
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26) // SQLITE_CORRUPT or SQLITE_NOTADB
        {
             _logger.LogCritical(ex, "Database corruption detected during oplog pruning (PruneOplogAsync).");
             try
             {
                 transaction.Rollback();
             }
             catch (Exception rollbackEx)
             {
                 _logger.LogError(rollbackEx, "Failed to rollback transaction after database corruption.");
             }
             throw new CorruptDatabaseException("SQLite database is corrupt", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prune oplog");
            throw;
        }
    }

    public async Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        // 1. Force a checkpoint to ensure WAL is merged (basic consistency)
        using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL);");
        }

        // 2. Safely copy the DB file
        var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;

        // We use a shared read lock approach or just copy. 
        // For strict consistency, we might need SQLite Online Backup API, but simple copy often suffices if WAL is checkpointed.
        using (var sourceStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            await sourceStream.CopyToAsync(destination, 81920, cancellationToken);
        }
    }

    public async Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Replacing database file from snapshot stream...");

        // Ensure connections are cleared
        SqliteConnection.ClearAllPools();

        var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var backupPath = dbPath + ".bak";

        try
        {
            // Backup current DB just in case
            if (File.Exists(dbPath))
            {
                File.Move(dbPath, backupPath);
            }

            // Write new DB
            using (var fileStream = File.Create(dbPath))
            {
                databaseStream.Seek(0, SeekOrigin.Begin); // Ensure stream is at start
                await databaseStream.CopyToAsync(fileStream, 81920, cancellationToken);
            }

            // Cleanup WAL/SHM to prevent corruption with new DB
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            // Re-initialize (force strict check)
            Initialize();

            // Cleanup backup if successful
            if (File.Exists(backupPath)) File.Delete(backupPath);

            _logger.LogInformation("Database replaced successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace database. Attempting restore from backup...");

            // Restore backup
            if (File.Exists(backupPath))
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                File.Move(backupPath, dbPath);
            }

            throw;
        }
    }

    public async Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Merging remote snapshot into local database...");

        // 1. Save stream to temp file
        var tempDbPath = Path.GetTempFileName();
        try
        {
            using (var fileStream = File.Create(tempDbPath))
            {
                snapshotStream.Seek(0, SeekOrigin.Begin);
                await snapshotStream.CopyToAsync(fileStream, 81920, cancellationToken);
            }

            // 2. Attach and Merge
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            await connection.ExecuteAsync($"ATTACH DATABASE '{tempDbPath}' AS remote_snapshot");

            using var transaction = connection.BeginTransaction();
            try
            {
                // Merge Oplog (Insert new, Ignore existing)
                await connection.ExecuteAsync(@"
                    INSERT OR IGNORE INTO main.Oplog (Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash)
                    SELECT Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash
                    FROM remote_snapshot.Oplog");

                // Merge SnapshotMetadata
                await connection.ExecuteAsync(@"
                    INSERT OR REPLACE INTO main.SnapshotMetadata (NodeId, HlcWall, HlcLogic, Hash)
                    SELECT NodeId, HlcWall, HlcLogic, Hash
                    FROM remote_snapshot.SnapshotMetadata
                    WHERE 1=1
                    ON CONFLICT(NodeId) DO UPDATE SET
                        HlcWall = MAX(HlcWall, excluded.HlcWall),
                        HlcLogic = MAX(HlcLogic, excluded.HlcLogic),
                        Hash = CASE WHEN excluded.HlcWall > HlcWall OR (excluded.HlcWall = HlcWall AND excluded.HlcLogic > HlcLogic) THEN excluded.Hash ELSE Hash END");

                if (_options?.UsePerCollectionTables != true)
                {
                    await connection.ExecuteAsync(@"
                        INSERT OR REPLACE INTO main.Documents (Collection, Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                        SELECT r.Collection, r.Key, r.JsonData, r.IsDeleted, r.HlcWall, r.HlcLogic, r.HlcNode
                        FROM remote_snapshot.Documents r
                        LEFT JOIN main.Documents l ON l.Collection = r.Collection AND l.Key = r.Key
                        WHERE l.Key IS NULL 
                           OR (r.HlcWall > l.HlcWall) 
                           OR (r.HlcWall = l.HlcWall AND r.HlcLogic > l.HlcLogic)");
                }
                else
                {
                     var tables = await connection.QueryAsync<string>("SELECT name FROM remote_snapshot.sqlite_master WHERE type='table' AND name LIKE 'Documents_%'");
                     foreach(var table in tables)
                     {
                         var collectionName = table.Substring(10);
                         await EnsureCollectionTablesAsync(connection, collectionName);
                         
                         await connection.ExecuteAsync($@"
                            INSERT OR REPLACE INTO main.{table} (Key, JsonData, IsDeleted, HlcWall, HlcLogic, HlcNode)
                            SELECT r.Key, r.JsonData, r.IsDeleted, r.HlcWall, r.HlcLogic, r.HlcNode
                            FROM remote_snapshot.{table} r
                            LEFT JOIN main.{table} l ON l.Key = r.Key
                            WHERE l.Key IS NULL 
                               OR (r.HlcWall > l.HlcWall) 
                               OR (r.HlcWall = l.HlcWall AND r.HlcLogic > l.HlcLogic)");
                     }
                }

                transaction.Commit();
            }
            finally
            {
                 await connection.ExecuteAsync("DETACH DATABASE remote_snapshot");
            }

            InitializeNodeCache(connection);
            _logger.LogInformation("Database merge completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge snapshot.");
            throw;
        }
        finally
        {
            if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
        }
    }

    public async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
         _logger.LogWarning("CLEARING ALL DATA FROM STORE!");
         using var connection = new SqliteConnection(_connectionString);
         await connection.OpenAsync(cancellationToken);
         
         await connection.ExecuteAsync("DELETE FROM Oplog");
         await connection.ExecuteAsync("DELETE FROM SnapshotMetadata");
         
         if (_options?.UsePerCollectionTables != true)
         {
             await connection.ExecuteAsync("DELETE FROM Documents");
         }
         else
         {
             var tables = await connection.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Documents_%'");
             foreach(var table in tables)
             {
                 await connection.ExecuteAsync($"DELETE FROM {table}");
             }
         }
         
         await connection.ExecuteAsync("VACUUM");
         
         lock (_cacheLock)
         {
             _nodeCache.Clear();
         }
         
         _logger.LogInformation("Store cleared successfully.");
    }
}

// Inner classes for Dapper mapping
internal class RemotePeerRow
{
    public string NodeId { get; set; } = "";
    public string Address { get; set; } = "";
    public int Type { get; set; }
    public string? OAuth2Json { get; set; }
    public int IsEnabled { get; set; }
}

internal class DocumentRow
{
    public string Key { get; set; } = "";
    public string? JsonData { get; set; }
    public int IsDeleted { get; set; }
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string HlcNode { get; set; } = "";
}

internal class OplogRow
{
    public string? Collection { get; set; }
    public string? Key { get; set; }
    public int Operation { get; set; }
    public string? JsonData { get; set; }
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string? HlcNode { get; set; }
    public string? Hash { get; set; }
    public string? PreviousHash { get; set; }
}

internal class OplogRowPerCollection
{
    public string? Key { get; set; }
    public int Operation { get; set; }
    public string? JsonData { get; set; }
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string? HlcNode { get; set; }
}

