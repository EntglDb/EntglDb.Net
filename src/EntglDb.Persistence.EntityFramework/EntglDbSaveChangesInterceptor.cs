using System;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// EF Core SaveChangesInterceptor that records CDC changes synchronously into IPendingChangesService.
/// Register via DbContextOptionsBuilder.AddInterceptors() as an alternative to the event-based
/// CDC path in EfCoreDocumentStore.
/// </summary>
public sealed class EntglDbSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IPendingChangesService _pendingChanges;
    private readonly Func<object, (string Collection, string Key)?> _collectionKeySelector;

    // NodeId is fixed at runtime — cached on first use.
    private readonly Lazy<string> _nodeId;

    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    public EntglDbSaveChangesInterceptor(
        IPendingChangesService pendingChanges,
        Func<object, (string Collection, string Key)?> collectionKeySelector,
        IPeerNodeConfigurationProvider configProvider)
    {
        _pendingChanges = pendingChanges ?? throw new ArgumentNullException(nameof(pendingChanges));
        _collectionKeySelector = collectionKeySelector ?? throw new ArgumentNullException(nameof(collectionKeySelector));
        _nodeId = new Lazy<string>(() => configProvider.GetConfiguration().GetAwaiter().GetResult().NodeId);
        _lastPhysicalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logicalCounter = 0;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RecordPendingChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RecordPendingChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void RecordPendingChanges(DbContext? context)
    {
        if (context == null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var collKey = _collectionKeySelector(entry.Entity);
            if (collKey == null) continue;

            var (collection, key) = collKey.Value;
            var opType = entry.State == EntityState.Deleted ? OperationType.Delete : OperationType.Put;
            _pendingChanges.RecordChange(collection, key, opType, GenerateTimestamp());
        }
    }

    private HlcTimestamp GenerateTimestamp()
    {
        var nodeId = _nodeId.Value;
        lock (_clockLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now > _lastPhysicalTime)
            {
                _lastPhysicalTime = now;
                _logicalCounter = 0;
            }
            else
            {
                _logicalCounter++;
            }
            return new HlcTimestamp(_lastPhysicalTime, _logicalCounter, nodeId);
        }
    }
}
