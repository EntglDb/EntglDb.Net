using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;

namespace EntglDb.Persistence.Sqlite;

public abstract class SnapshotMetadataStore : ISnapshotMetadataStore
{
    public abstract Task DropAsync(CancellationToken cancellationToken = default);

    public abstract Task<IEnumerable<SnapshotMetadata>> ExportAsync(CancellationToken cancellationToken = default);

    public abstract Task<SnapshotMetadata?> FindByNodeIs(string nodeId, CancellationToken cancellationToken);

    public abstract Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default);

    public abstract Task ImportAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default);

    public abstract Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default);

    public abstract Task MergeAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default);

    public abstract Task UpdateSnapshotMetadataAsync(SnapshotMetadata existingMeta, CancellationToken cancellationToken);
}

