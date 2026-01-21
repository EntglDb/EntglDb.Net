using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Persistence.PostgreSQL;

/// <summary>
/// PostgreSQL-specific DbContext with JSONB configuration.
/// </summary>
public class PostgreSqlDbContext : EntglDbContext
{
    public PostgreSqlDbContext(DbContextOptions<EntglDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure JSONB columns for PostgreSQL
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.Property(e => e.ContentJson)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<OplogEntity>(entity =>
        {
            entity.Property(e => e.PayloadJson)
                .HasColumnType("jsonb");
        });

        // Note: GIN indexes should be created via migrations for better control
        // Example migration code:
        // migrationBuilder.Sql(@"
        //     CREATE INDEX IF NOT EXISTS IX_Documents_ContentJson_gin 
        //     ON ""Documents"" USING GIN (""ContentJson"" jsonb_path_ops);
        // 
        //     CREATE INDEX IF NOT EXISTS IX_Oplog_PayloadJson_gin 
        //     ON ""Oplog"" USING GIN (""PayloadJson"" jsonb_path_ops);
        // ");
    }
}
