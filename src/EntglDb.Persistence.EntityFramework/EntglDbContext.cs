using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Entity Framework DbContext for EntglDb persistence.
/// Supports SQL Server, PostgreSQL, MySQL, and SQLite.
/// </summary>
public class EntglDbContext : DbContext
{
    /// <summary>
    /// Gets or sets the Documents DbSet.
    /// </summary>
    public DbSet<DocumentEntity> Documents { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Oplog DbSet.
    /// </summary>
    public DbSet<OplogEntity> Oplog { get; set; } = null!;

    /// <summary>
    /// Gets or sets the RemotePeers DbSet.
    /// </summary>
    public DbSet<RemotePeerEntity> RemotePeers { get; set; } = null!;

    /// <summary>
    /// Gets or sets the SnapshotMetadata DbSet.
    /// </summary>
    public DbSet<SnapshotMetadataEntity> SnapshotMetadata { get; set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntglDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to configure the context.</param>
    public EntglDbContext(DbContextOptions<EntglDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Configures the entity models.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure DocumentEntity
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.HasKey(e => new { e.Collection, e.Key });
            entity.HasIndex(e => new { e.UpdatedAtPhysicalTime, e.UpdatedAtLogicalCounter, e.UpdatedAtNodeId });
            entity.HasIndex(e => e.IsDeleted);
        });

        // Configure OplogEntity
        modelBuilder.Entity<OplogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId });
            entity.HasIndex(e => e.Collection);
            entity.HasIndex(e => e.Hash);
        });

        // Configure RemotePeerEntity
        modelBuilder.Entity<RemotePeerEntity>(entity =>
        {
            entity.HasKey(e => e.NodeId);
            entity.HasIndex(e => e.IsEnabled);
        });

        // Configure SnapshotMetadataEntity
        modelBuilder.Entity<SnapshotMetadataEntity>(entity =>
        {
            entity.HasKey(e => e.NodeId);
            entity.HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter });
        });
    }
}
