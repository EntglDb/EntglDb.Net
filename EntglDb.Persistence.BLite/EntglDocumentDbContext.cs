using BLite.Core;
using BLite.Core.Collections;
using BLite.Core.Metadata;
using BLite.Core.Storage;
using EntglDb.Persistence.BLite.Entities;

namespace EntglDb.Persistence.BLite;

public partial class EntglDocumentDbContext : DocumentDbContext
{
    /// <summary>
    /// Gets the collection of operation log entries associated with this instance.
    /// </summary>
    /// <remarks>The collection provides access to all recorded operation log (oplog) entries, which can be
    /// used to track changes or replicate operations. The collection is read-only; entries cannot be added or removed
    /// directly through this property.</remarks>
    public DocumentCollection<string, OplogEntity> OplogEntries { get; private set; } = null!;

    /// <summary>
    /// Gets the collection of snapshot metadata associated with the document.
    /// </summary>
    public DocumentCollection<string, SnapshotMetadataEntity> SnapshotMetadatas { get; private set; } = null!;

    /// <summary>
    /// Gets the collection of remote peer configurations associated with this instance.
    /// </summary>
    /// <remarks>Use this collection to access or enumerate the configuration settings for each remote peer.
    /// The collection is read-only; to modify peer configurations, use the appropriate methods provided by the
    /// containing class.</remarks>
    public DocumentCollection<string, RemotePeerEntity> RemotePeerConfigurations { get; private set; } = null!;

    /// <summary>
    /// Gets the collection of document metadata for sync tracking.
    /// </summary>
    /// <remarks>Stores HLC timestamps and deleted state for each document without modifying application entities.
    /// Used to track document versions for incremental sync instead of full snapshots.</remarks>
    public DocumentCollection<string, DocumentMetadataEntity> DocumentMetadatas { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of the EntglDocumentDbContext class using the specified database file path.
    /// </summary>
    /// <param name="databasePath">The file system path to the database file to be used by the context. Cannot be null or empty.</param>
    public EntglDocumentDbContext(string databasePath) : base(databasePath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the EntglDocumentDbContext class using the specified database path and page file
    /// configuration.
    /// </summary>
    /// <param name="databasePath">The file system path to the database file. This value cannot be null or empty.</param>
    /// <param name="config">The configuration settings for the page file. Specifies options that control how the database pages are managed.</param>
    public EntglDocumentDbContext(string databasePath, PageFileConfig config) : base(databasePath, config)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // OplogEntries: Use Id as technical key, Hash as unique business key
        modelBuilder.Entity<OplogEntity>()
            .ToCollection("OplogEntries")
            .HasKey(e => e.Id)
            .HasIndex(e => e.Hash, unique: true) // Hash is unique business key
            .HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId })
            .HasIndex(e => e.Collection);

        // SnapshotMetadatas: Use Id as technical key, NodeId as unique business key
        modelBuilder.Entity<SnapshotMetadataEntity>()
            .ToCollection("SnapshotMetadatas")
            .HasKey(e => e.Id)
            .HasIndex(e => e.NodeId, unique: true) // NodeId is unique business key
            .HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter });

        // RemotePeerConfigurations: Use Id as technical key, NodeId as unique business key
        modelBuilder.Entity<RemotePeerEntity>()
            .ToCollection("RemotePeerConfigurations")
            .HasKey(e => e.Id)
            .HasIndex(e => e.NodeId, unique: true) // NodeId is unique business key
            .HasIndex(e => e.IsEnabled);

        // DocumentMetadatas: Use Id as technical key, Collection+Key as unique composite business key
        modelBuilder.Entity<DocumentMetadataEntity>()
            .ToCollection("DocumentMetadatas")
            .HasKey(e => e.Id)
            .HasIndex(e => new { e.Collection, e.Key }, unique: true) // Composite business key
            .HasIndex(e => new { e.HlcPhysicalTime, e.HlcLogicalCounter, e.HlcNodeId })
            .HasIndex(e => e.Collection);
    }
}
