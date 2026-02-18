using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EntglDb.Core;

namespace EntglDb.Persistence.EntityFramework;

public class OplogEntityConfiguration : IEntityTypeConfiguration<OplogEntity>
{
    public void Configure(EntityTypeBuilder<OplogEntity> builder)
    {
        // Configure OplogEntity
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId });
        builder.HasIndex(e => e.Collection);
        builder.HasIndex(e => e.Hash);
    }
}
