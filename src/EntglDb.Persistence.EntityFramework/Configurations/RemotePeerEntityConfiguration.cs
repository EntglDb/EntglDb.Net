using Microsoft.EntityFrameworkCore;
using EntglDb.Persistence.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntglDb.Persistence.EntityFramework;

public class RemotePeerEntityConfiguration : IEntityTypeConfiguration<RemotePeerEntity>
{
    public void Configure(EntityTypeBuilder<RemotePeerEntity> builder)
    {
        // Configure RemotePeerEntity
        builder.HasKey(e => e.NodeId);
        builder.HasIndex(e => e.IsEnabled);
    }
}
