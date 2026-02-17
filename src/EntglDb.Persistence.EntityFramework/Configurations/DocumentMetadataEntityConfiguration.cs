using EntglDb.Persistence.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// EF Core configuration for DocumentMetadataEntity.
/// </summary>
public class DocumentMetadataEntityConfiguration : IEntityTypeConfiguration<DocumentMetadataEntity>
{
    public void Configure(EntityTypeBuilder<DocumentMetadataEntity> builder)
    {
        builder.ToTable("DocumentMetadata");

        builder.HasKey(e => e.Id);

        // Unique index on (Collection, Key) for fast lookups
        builder.HasIndex(e => new { e.Collection, e.Key })
            .IsUnique()
            .HasDatabaseName("IX_DocumentMetadata_Collection_Key");
    }
}
