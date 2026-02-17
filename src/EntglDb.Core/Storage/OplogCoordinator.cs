using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Core.Storage.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Storage;

/// <summary>
/// Coordinates local document changes with the operation log and document metadata:
/// - Listens to IDocumentStore events (insert/update/delete)
/// - Creates OplogEntry records with proper HLC timestamps
/// - Registers them in the IOplogStore
/// - Maintains the hash chain for this node
/// - Updates document metadata for sync tracking
/// </summary>
public class OplogCoordinator : IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly IOplogStore _oplogStore;
    private readonly IDocumentMetadataStore? _documentMetadataStore;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly ILogger<OplogCoordinator> _logger;
    private bool _disposed;
    
    // HLC state for this node
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    /// <summary>
    /// Creates an OplogCoordinator with optional document metadata tracking.
    /// </summary>
    /// <param name="documentStore">The document store to listen to.</param>
    /// <param name="oplogStore">The oplog store to write entries to.</param>
    /// <param name="configProvider">Provider for node configuration.</param>
    /// <param name="documentMetadataStore">Optional metadata store for sync tracking. If null, metadata will not be tracked.</param>
    /// <param name="logger">Optional logger.</param>
    public OplogCoordinator(
        IDocumentStore documentStore,
        IOplogStore oplogStore,
        IPeerNodeConfigurationProvider configProvider,
        IDocumentMetadataStore? documentMetadataStore = null,
        ILogger<OplogCoordinator>? logger = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _oplogStore = oplogStore ?? throw new ArgumentNullException(nameof(oplogStore));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _documentMetadataStore = documentMetadataStore;
        _logger = logger ?? NullLogger<OplogCoordinator>.Instance;

        _lastPhysicalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logicalCounter = 0;

        // Subscribe to document store events to track local changes
        _documentStore.DocumentsInserted += OnDocumentsInserted;
        _documentStore.DocumentsUpdated += OnDocumentsUpdated;
        _documentStore.DocumentsDeleted += OnDocumentsDeleted;

        _logger.LogInformation("OplogCoordinator initialized - tracking local document changes{MetadataStatus}",
            _documentMetadataStore != null ? " with metadata tracking" : "");
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

        var metadatasToUpdate = new List<DocumentMetadata>();

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

                // Track metadata for batch update
                if (_documentMetadataStore != null)
                {
                    metadatasToUpdate.Add(new DocumentMetadata(
                        document.Collection,
                        document.Key,
                        timestamp,
                        isDeleted: false
                    ));
                }

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

        // Batch update document metadata
        if (_documentMetadataStore != null && metadatasToUpdate.Count > 0)
        {
            try
            {
                await _documentMetadataStore.UpsertMetadataBatchAsync(metadatasToUpdate);
                _logger.LogDebug("Updated metadata for {Count} documents", metadatasToUpdate.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update document metadata batch");
            }
        }
    }

    private async Task ProcessDocumentDeletionsAsync(string collection, IEnumerable<string> documentKeys)
    {
        var config = await _configProvider.GetConfiguration();
        var nodeId = config.NodeId;

        var keysToMarkDeleted = new List<(string Key, HlcTimestamp Timestamp)>();

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

                // Track for metadata update
                if (_documentMetadataStore != null)
                {
                    keysToMarkDeleted.Add((key, timestamp));
                }

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

        // Update document metadata to mark as deleted
        if (_documentMetadataStore != null && keysToMarkDeleted.Count > 0)
        {
            try
            {
                foreach (var (key, timestamp) in keysToMarkDeleted)
                {
                    await _documentMetadataStore.MarkDeletedAsync(collection, key, timestamp);
                }
                _logger.LogDebug("Marked {Count} documents as deleted in metadata", keysToMarkDeleted.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark documents as deleted in metadata");
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
