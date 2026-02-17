using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Core.Storage.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Storage;

/// <summary>
/// Coordinates local document changes with the operation log:
/// - Listens to IDocumentStore events (insert/update/delete)
/// - Creates OplogEntry records with proper HLC timestamps
/// - Registers them in the IOplogStore
/// - Maintains the hash chain for this node
/// </summary>
public class OplogCoordinator : IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly IOplogStore _oplogStore;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly ILogger<OplogCoordinator> _logger;
    private bool _disposed;
    
    // HLC state for this node
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    public OplogCoordinator(
        IDocumentStore documentStore,
        IOplogStore oplogStore,
        IPeerNodeConfigurationProvider configProvider,
        ILogger<OplogCoordinator>? logger = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _oplogStore = oplogStore ?? throw new ArgumentNullException(nameof(oplogStore));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _logger = logger ?? NullLogger<OplogCoordinator>.Instance;

        _lastPhysicalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logicalCounter = 0;

        // Subscribe to document store events to track local changes
        _documentStore.DocumentsInserted += OnDocumentsInserted;
        _documentStore.DocumentsUpdated += OnDocumentsUpdated;
        _documentStore.DocumentsDeleted += OnDocumentsDeleted;

        _logger.LogInformation("OplogCoordinator initialized - tracking local document changes");
    }
    
    /// <summary>
    /// Generates a new HLC timestamp for local events.
    /// Implements the HLC algorithm: advance physical time if wall clock moved forward, 
    /// otherwise increment logical counter.
    /// </summary>
    private HlcTimestamp GenerateTimestamp(string nodeId)
    {
        lock (_clockLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            if (now > _lastPhysicalTime)
            {
                // Physical time advanced - reset logical counter
                _lastPhysicalTime = now;
                _logicalCounter = 0;
            }
            else
            {
                // Physical time same or went backwards - increment logical counter
                _logicalCounter++;
            }
            
            return new HlcTimestamp(_lastPhysicalTime, _logicalCounter, nodeId);
        }
    }

    private async void OnDocumentsInserted(object? sender, DocumentsInsertedEventArgs e)
    {
        try
        {
            await ProcessDocumentChangesAsync(e.Documents, OperationType.Put);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inserted documents from collection {Collection}", e.Collection);
        }
    }

    private async void OnDocumentsUpdated(object? sender, DocumentsUpdatedEventArgs e)
    {
        try
        {
            await ProcessDocumentChangesAsync(e.Documents, OperationType.Put);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing updated documents from collection {Collection}", e.Collection);
        }
    }

    private async void OnDocumentsDeleted(object? sender, DocumentsDeletedEventArgs e)
    {
        try
        {
            await ProcessDocumentDeletionsAsync(e.Collection, e.DocumentKeys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing deleted documents from collection {Collection}", e.Collection);
        }
    }

    private async Task ProcessDocumentChangesAsync(IEnumerable<Document> documents, OperationType operationType)
    {
        var config = await _configProvider.GetConfiguration();
        var nodeId = config.NodeId;

        foreach (var document in documents)
        {
            try
            {
                // Get the last hash for this node to maintain the hash chain
                var previousHash = await _oplogStore.GetLastEntryHashAsync(nodeId) ?? string.Empty;

                // Generate HLC timestamp
                var timestamp = GenerateTimestamp(nodeId);

                // Create oplog entry
                var oplogEntry = new OplogEntry(
                    document.Collection,
                    document.Key,
                    operationType,
                    document.Content,
                    timestamp,
                    previousHash
                );

                // Append to oplog - this maintains the chain and updates the cache
                await _oplogStore.AppendOplogEntryAsync(oplogEntry);

                _logger.LogDebug(
                    "Registered local {Operation} for {Collection}/{Key} at {Timestamp} (hash: {Hash})",
                    operationType,
                    document.Collection,
                    document.Key,
                    timestamp,
                    oplogEntry.Hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to register oplog entry for {Collection}/{Key}",
                    document.Collection,
                    document.Key);
            }
        }
    }

    private async Task ProcessDocumentDeletionsAsync(string collection, IEnumerable<string> documentKeys)
    {
        var config = await _configProvider.GetConfiguration();
        var nodeId = config.NodeId;

        foreach (var key in documentKeys)
        {
            try
            {
                // Get the last hash for this node to maintain the hash chain
                var previousHash = await _oplogStore.GetLastEntryHashAsync(nodeId) ?? string.Empty;

                // Generate HLC timestamp
                var timestamp = GenerateTimestamp(nodeId);

                // Create oplog entry for deletion (no payload)
                var oplogEntry = new OplogEntry(
                    collection,
                    key,
                    OperationType.Delete,
                    null,
                    timestamp,
                    previousHash
                );

                // Append to oplog
                await _oplogStore.AppendOplogEntryAsync(oplogEntry);

                _logger.LogDebug(
                    "Registered local Delete for {Collection}/{Key} at {Timestamp} (hash: {Hash})",
                    collection,
                    key,
                    timestamp,
                    oplogEntry.Hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to register deletion oplog entry for {Collection}/{Key}",
                    collection,
                    key);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Unsubscribe from events to prevent memory leaks
        _documentStore.DocumentsInserted -= OnDocumentsInserted;
        _documentStore.DocumentsUpdated -= OnDocumentsUpdated;
        _documentStore.DocumentsDeleted -= OnDocumentsDeleted;

        _disposed = true;
        _logger.LogInformation("OplogCoordinator disposed");
    }
}
