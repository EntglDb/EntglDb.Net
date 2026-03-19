using BLite.Core.Collections;
using BLite.Core.Metadata;
using BLite.Core.Storage;
using EntglDb.Persistence.Entities;
using EntglDb.Persistence.BLite.Entities;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Internal BLite context for EntglDb metadata management.
/// Stores oplog, peers, snapshots, document metadata, and pending changes for the sync system.
/// This context is private to EntglDb and not exposed to user applications.
/// </summary>
public sealed partial class EntglDbMetaContext : EntglDocumentDbContext
{
    /// <summary>
    /// Initializes a new instance of the EntglDbMetaContext class for EntglDb internal metadata.
    /// </summary>
    /// <param name="databasePath">The file system path to the metadata database file.</param>
    public EntglDbMetaContext(string databasePath) : base(databasePath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the EntglDbMetaContext class with custom page file configuration.
    /// </summary>
    /// <param name="databasePath">The file system path to the metadata database file.</param>
    /// <param name="config">The page file configuration for the database.</param>
    public EntglDbMetaContext(string databasePath, PageFileConfig config) : base(databasePath, config)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PendingChanges: Use Id as business key ("collection/key"), collection+key for lookup
        modelBuilder.Entity<PendingChangeEntity>()
            .ToCollection("PendingChanges")
            .HasKey(e => e.Id)
            .HasIndex(e => new { e.Collection, e.Key }, unique: false) // For batch queries
            .HasIndex(e => new { e.HlcPhysicalTime, e.HlcLogicalCounter, e.HlcNodeId });
    }
}
