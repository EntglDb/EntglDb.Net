using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;

namespace EntglDb.Persistence.Sqlite;

/// <summary>
/// Thread-safe in-memory cache for Vector Clock state.
/// Updated by DocumentStore (local CDC) and OplogStore (remote sync).
/// </summary>
public class VectorClockService : IVectorClockService
{
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly Dictionary<string, NodeCacheEntry> _cache = new Dictionary<string, NodeCacheEntry>(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Update(OplogEntry entry)
    {
        _lock.Wait();
        try
        {
            var nodeId = entry.Timestamp.NodeId;
            if (!_cache.TryGetValue(nodeId, out var existing) || entry.Timestamp.CompareTo(existing.Timestamp) > 0)
            {
                _cache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = entry.Timestamp,
                    Hash = entry.Hash ?? ""
                };
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void UpdateNode(string nodeId, HlcTimestamp timestamp, string hash)
    {
        _lock.Wait();
        try
        {
            if (!_cache.TryGetValue(nodeId, out var existing) || timestamp.CompareTo(existing.Timestamp) > 0)
            {
                _cache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = timestamp,
                    Hash = hash
                };
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
    {
        _lock.Wait();
        try
        {
            var vectorClock = new VectorClock();
            foreach (var kvp in _cache)
            {
                vectorClock.SetTimestamp(kvp.Key, kvp.Value.Timestamp);
            }
            return Task.FromResult(vectorClock);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
    {
        _lock.Wait();
        try
        {
            if (_cache.Count == 0)
            {
                return Task.FromResult(new HlcTimestamp(0, 0, ""));
            }

            var maxTimestamp = _cache.Values
                .Select(e => e.Timestamp)
                .OrderByDescending(t => t)
                .First();

            return Task.FromResult(maxTimestamp);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public string? GetLastHash(string nodeId)
    {
        _lock.Wait();
        try
        {
            return _cache.TryGetValue(nodeId, out var entry) ? entry.Hash : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _lock.Wait();
        try
        {
            _cache.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }
}
