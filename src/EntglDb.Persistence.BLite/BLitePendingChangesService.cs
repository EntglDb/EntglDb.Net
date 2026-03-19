using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Entities;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// In-memory implementation of IPendingChangesService using ConcurrentDictionary.
/// Stores one entry per (collection, key) pair with upsert semantics (last-write wins).
/// Thread-safe and non-blocking for RecordChange (zero I/O).
/// </summary>
public class BLitePendingChangesService : IPendingChangesService
{
    private readonly ConcurrentDictionary<string, PendingChangeEntity> _pendingByKey = new();

    /// <summary>
    /// Records a local change synchronously (in-memory, zero I/O).
    /// Uses upsert semantics: Id = "collection/key", last write wins.
    /// </summary>
    public void RecordChange(string collection, string key, OperationType operationType, HlcTimestamp timestamp)
    {
        if (string.IsNullOrWhiteSpace(collection)) throw new ArgumentException("collection is required", nameof(collection));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));

        var id = $"{collection}/{key}";
        var entity = new PendingChangeEntity
        {
            Id = id,
            Collection = collection,
            Key = key,
            OperationType = (int)operationType,
            HlcPhysicalTime = timestamp.PhysicalTime,
            HlcLogicalCounter = timestamp.LogicalCounter,
            HlcNodeId = timestamp.NodeId
        };

        _pendingByKey[id] = entity;
    }

    /// <summary>
    /// Atomically retrieves all pending changes and clears the buffer.
    /// Returns a snapshot of pending changes; caller is responsible for flushing them to oplog.
    /// </summary>
    public Task<IReadOnlyList<PendingChange>> PopAllAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot all pending changes
        var snapshot = _pendingByKey.Values.ToList();

        // Convert entities to domain model
        var pendingChanges = snapshot.Select(e => new PendingChange(
            e.Collection,
            e.Key,
            (OperationType)e.OperationType,
            new HlcTimestamp(e.HlcPhysicalTime, e.HlcLogicalCounter, e.HlcNodeId)
        )).ToList();

        // Clear all pending entries
        _pendingByKey.Clear();

        return Task.FromResult<IReadOnlyList<PendingChange>>(pendingChanges);
    }
}
