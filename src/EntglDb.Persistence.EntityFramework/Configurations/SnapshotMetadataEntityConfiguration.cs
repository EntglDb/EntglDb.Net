using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntglDb.Persistence.EntityFramework;

public class SnapshotMetadataEntityConfiguration : IEntityTypeConfiguration<SnapshotMetadataEntity>
{
    public void Configure(EntityTypeBuilder<SnapshotMetadataEntity> builder)
    {
        // Configure SnapshotMetadataEntity
        builder.HasKey(e => e.NodeId);
        builder.HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter });
    }
}
