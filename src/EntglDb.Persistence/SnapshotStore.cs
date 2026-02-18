using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.Snapshot;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Persistence.Sqlite;

public class SnapshotStore : ISnapshotService
{
    /// <summary>
    /// Represents the document store used for data persistence and retrieval operations.
    /// </summary>
    protected readonly IDocumentStore _documentStore;
    /// <summary>
    /// Provides access to the peer configuration store used for retrieving and managing peer configuration data.
    /// </summary>
    protected readonly IPeerConfigurationStore _peerConfigurationStore;
    /// <summary>
    /// Provides access to the underlying oplog store used for persisting and retrieving operation logs.
    /// </summary>
    protected readonly IOplogStore _oplogStore;
    /// <summary>
    /// Provides access to the conflict resolution strategy used by the containing class.
    /// </summary>
    /// <remarks>This field is intended for use by derived classes to resolve conflicts according to the logic
    /// defined by the associated <see cref="IConflictResolver"/> implementation. The specific behavior depends on the
    /// implementation provided.</remarks>
    protected readonly IConflictResolver _conflictResolver;
    /// <summary>
    /// The logger instance used for logging
    /// </summary>
    protected readonly ILogger<SnapshotStore> _logger;

    /// <summary>
    /// Initializes a new instance of the SnapshotStore class using the specified document, peer configuration, and
    /// oplog stores, conflict resolver, and optional logger.
    /// </summary>
    /// <param name="documentStore">The document store used to persist and retrieve documents for snapshots. Cannot be null.</param>
    /// <param name="peerConfigurationStore">The peer configuration store used to manage peer settings and metadata. Cannot be null.</param>
    /// <param name="oplogStore">The oplog store used to track and apply operation logs for snapshot consistency. Cannot be null.</param>
    /// <param name="conflictResolver">The conflict resolver used to handle conflicts during snapshot creation and restoration. Cannot be null.</param>
    /// <param name="logger">The optional logger used for logging diagnostic and operational information. If null, a default logger is used.</param>
    /// <exception cref="ArgumentNullException">Thrown if any of the parameters documentStore, peerConfigurationStore, oplogStore, or conflictResolver is null.</exception>
    public SnapshotStore(
        IDocumentStore documentStore,
        IPeerConfigurationStore peerConfigurationStore,
        IOplogStore oplogStore,
        IConflictResolver conflictResolver,
        ILogger<SnapshotStore>? logger = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _peerConfigurationStore = peerConfigurationStore ?? throw new ArgumentNullException(nameof(peerConfigurationStore));
        _oplogStore = oplogStore ?? throw new ArgumentNullException(nameof(oplogStore));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SnapshotStore>.Instance;
    }

    private async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        await _documentStore.DropAsync(cancellationToken);
        await _peerConfigurationStore.DropAsync(cancellationToken);
        await _oplogStore.DropAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating snapshot...");
        
        var documents = await _documentStore.ExportAsync(cancellationToken);
        var remotePeers = await _peerConfigurationStore.ExportAsync(cancellationToken);
        var oplogEntries = await _oplogStore.ExportAsync(cancellationToken);

        var snapshot = new SnapshotDto
        {
            Version = "1.0",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            NodeId = "", // Will be set by caller if needed
            Documents = [.. documents.Select(d => new DocumentDto
            {
                Collection = d.Collection,
                Key = d.Key,
                JsonData = d.Content.ToString(),
                IsDeleted = d.IsDeleted,
                HlcWall = d.UpdatedAt.PhysicalTime,
                HlcLogic = d.UpdatedAt.LogicalCounter,
                HlcNode = d.UpdatedAt.NodeId
            })],
            Oplog = [.. oplogEntries.Select(o => new OplogDto
            {
                Collection = o.Collection,
                Key = o.Key,
                Operation = (int)o.Operation,
                JsonData = o.Payload?.ToString() ?? "",
                HlcWall = o.Timestamp.PhysicalTime,
                HlcLogic = o.Timestamp.LogicalCounter,
                HlcNode = o.Timestamp.NodeId,
                Hash = o.Hash ?? "",
                PreviousHash = o.PreviousHash
            })],
            SnapshotMetadata = [], // Can be filled in by caller if needed
            RemotePeers = [.. remotePeers.Select(p => new RemotePeerDto
            {
                NodeId = p.NodeId,
                Address = p.Address,
                Type = (int)p.Type,
                OAuth2Json = p.OAuth2Json,
                IsEnabled = p.IsEnabled
            })]
        };

        // Serialize snapshot to the destination stream
        await JsonSerializer.SerializeAsync(destination, snapshot, cancellationToken: cancellationToken);
        await destination.FlushAsync(cancellationToken);
        
        _logger.LogInformation("Snapshot created: {DocumentCount} documents, {OplogCount} oplog entries",
            snapshot.Documents.Count, snapshot.Oplog.Count);
    }

    /// <inheritdoc />
    public async Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Replacing data from snapshot stream...");

        await ClearAllDataAsync(cancellationToken);

        var snapshot = await JsonSerializer.DeserializeAsync<SnapshotDto>(databaseStream, cancellationToken: cancellationToken);
        if (snapshot == null) throw new InvalidOperationException("Failed to deserialize snapshot");

        var documents = snapshot.Documents.Select(d => new Document(
            d.Collection,
            d.Key,
            JsonDocument.Parse(d.JsonData ?? "{}").RootElement,
            new HlcTimestamp(d.HlcWall, d.HlcLogic, d.HlcNode),
            d.IsDeleted)).ToList();

        var oplogEntries = snapshot.Oplog.Select(o => new OplogEntry(
            o.Collection,
            o.Key,
            (OperationType)o.Operation,
            JsonDocument.Parse(o.JsonData ?? "{}").RootElement,
            new HlcTimestamp(o.HlcWall, o.HlcLogic, o.HlcNode),
            o.Hash,
            o.PreviousHash)).ToList();

        var remotePeers = snapshot.RemotePeers.Select(p => new RemotePeerConfiguration
        {
            NodeId = p.NodeId,
            Address = p.Address,
            Type = (PeerType)p.Type,
            OAuth2Json = p.OAuth2Json,
            IsEnabled = p.IsEnabled,
            InterestingCollections = []
        }).ToList();

        await _documentStore.ImportAsync(documents, cancellationToken);
        await _oplogStore.ImportAsync(oplogEntries, cancellationToken);
        await _peerConfigurationStore.ImportAsync(remotePeers, cancellationToken);

        _logger.LogInformation("Database replaced successfully.");
    }

    /// <inheritdoc />
    public async Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Merging snapshot from stream...");
        var snapshot = await JsonSerializer.DeserializeAsync<SnapshotDto>(snapshotStream, cancellationToken: cancellationToken);
        if (snapshot == null) throw new InvalidOperationException("Failed to deserialize snapshot");
        var documents = snapshot.Documents.Select(d => new Document(
            d.Collection,
            d.Key,
            JsonDocument.Parse(d.JsonData ?? "{}").RootElement,
            new HlcTimestamp(d.HlcWall, d.HlcLogic, d.HlcNode),
            d.IsDeleted)).ToList();
        var oplogEntries = snapshot.Oplog.Select(o => new OplogEntry(
            o.Collection,
            o.Key,
            (OperationType)o.Operation,
            JsonDocument.Parse(o.JsonData ?? "{}").RootElement,
            new HlcTimestamp(o.HlcWall, o.HlcLogic, o.HlcNode),
            o.Hash,
            o.PreviousHash)).ToList();
        var remotePeers = snapshot.RemotePeers.Select(p => new RemotePeerConfiguration
        {
            NodeId = p.NodeId,
            Address = p.Address,
            Type = (PeerType)p.Type,
            OAuth2Json = p.OAuth2Json,
            IsEnabled = p.IsEnabled,
            InterestingCollections = []
        }).ToList();

        await _documentStore.MergeAsync(documents, cancellationToken);
        await _oplogStore.MergeAsync(oplogEntries, cancellationToken);
        await _peerConfigurationStore.MergeAsync(remotePeers, cancellationToken);

        _logger.LogInformation("Snapshot merged successfully.");
    }
}