using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Sample.Shared;

/// <summary>
/// EF Core DbContext for EntglDb Sample application.
/// Manages Users and TodoLists as well as EntglDb internal tables (Oplog, Peers, Snapshots).
/// </summary>
public class SampleEfCoreDbContext : DbContext
{
    private readonly string _databasePath;

    public SampleEfCoreDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    // Application entities
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<TodoList> TodoLists { get; set; } = null!;

    // EntglDb internal entities
    public DbSet<OplogEntity> OplogEntries { get; set; } = null!;
    public DbSet<RemotePeerEntity> RemotePeers { get; set; } = null!;
    public DbSet<SnapshotMetadataEntity> SnapshotMetadatas { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure application entities
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.OwnsOne(e => e.Address);
        });

        modelBuilder.Entity<TodoList>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.OwnsMany(e => e.Items, item =>
            {
                item.Property(i => i.Task).IsRequired();
            });
        });

        // Configure EntglDb internal entities
        modelBuilder.ApplyEntglDbEntityFrameworkConfigurations();
    }
}
