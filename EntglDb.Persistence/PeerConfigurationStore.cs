using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Storage;
using EntglDb.Core.Network;

namespace EntglDb.Persistence.Sqlite;

public abstract class PeerConfigurationStore : IPeerConfigurationStore
{
    /// <inheritdoc />
    public abstract Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken);

    /// <inheritdoc />
    public abstract Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default);

    public abstract Task DropAsync(CancellationToken cancellationToken = default);

    public abstract Task<IEnumerable<RemotePeerConfiguration>> ExportAsync(CancellationToken cancellationToken = default);

    public virtual async Task ImportAsync(IEnumerable<RemotePeerConfiguration> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await SaveRemotePeerAsync(item, cancellationToken);
        }
    }

    public virtual async Task MergeAsync(IEnumerable<RemotePeerConfiguration> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var existing = await GetRemotePeerAsync(item.NodeId, cancellationToken);
            if (existing == null)
            {
                await SaveRemotePeerAsync(item, cancellationToken);
            }
            // If exists, keep existing (simple merge strategy)
        }
    }
}

