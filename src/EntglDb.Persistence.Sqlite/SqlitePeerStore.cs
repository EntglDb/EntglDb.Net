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
            
        // Update local cache
        lock (_cacheLock)
        {
             if (entry.Timestamp.CompareTo(_cachedTimestamp) > 0)
             {
                 _cachedTimestamp = entry.Timestamp;
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
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        return await connection.QuerySingleOrDefaultAsync<string>(@"
            SELECT Hash 
            FROM Oplog 
            WHERE HlcNode = @NodeId 
            ORDER BY HlcWall DESC, HlcLogic DESC 
            LIMIT 1", new { NodeId = nodeId });
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
            new { 
                NodeId = startRow.HlcNode,
                StartWall = startRow.HlcWall, StartLogic = startRow.HlcLogic,
                EndWall = endRow.HlcWall, EndLogic = endRow.HlcLogic
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
         // Unified Oplog: Always query the single Oplog table
         var row = connection.QuerySingleOrDefault<MaxHlcResult>(@"
             SELECT MAX(HlcWall) as Wall, MAX(HlcLogic) as Logic, HlcNode 
             FROM Oplog
             ORDER BY HlcWall DESC, HlcLogic DESC LIMIT 1");
         
         if (row == null || row.Wall == null) return new HlcTimestamp(0, 0, "");
         return new HlcTimestamp(row.Wall.Value, row.Logic ?? 0, row.HlcNode ?? "");
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
                
                // Unified Oplog Table: Always use single Oplog table regardless of per-collection setting
                await connection.ExecuteAsync(@"
                    INSERT INTO Oplog (Collection, Key, Operation, JsonData, HlcWall, HlcLogic, HlcNode, Hash, PreviousHash)
                    VALUES (@Collection, @Key, @Operation, @JsonData, @HlcWall, @HlcLogic, @HlcNode, @Hash, @PreviousHash)",
                    new
                    {
                        entry.Collection,
                        entry.Key,
                        Operation = (int)entry.Operation,
                        JsonData = entry.Payload != null && entry.Payload!.Value.ValueKind != JsonValueKind.Undefined ? entry.Payload.Value.GetRawText() : null,
                        HlcWall = entry.Timestamp.PhysicalTime,
                        HlcLogic = entry.Timestamp.LogicalCounter,
                        HlcNode = entry.Timestamp.NodeId,
                        entry.Hash,
                        entry.PreviousHash
                    }, transaction);
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
        public string? Hash { get; set; }
        public string? PreviousHash { get; set; }
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
}
