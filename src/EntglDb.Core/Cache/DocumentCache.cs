using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core;

namespace EntglDb.Core.Cache
{
    /// <summary>
    /// LRU cache entry with linked list node.
    /// </summary>
    internal class CacheEntry
    {
        public Document Document { get; }
        public LinkedListNode<string> Node { get; }

        public CacheEntry(Document document, LinkedListNode<string> node)
        {
            Document = document;
            Node = node;
        }
    }

    /// <summary>
    /// In-memory LRU cache for documents.
    /// </summary>
    public class DocumentCache
    {
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly LinkedList<string> _lru = new();
        private readonly ILogger<DocumentCache> _logger;
        private readonly int _maxSize;
        private readonly object _lock = new();
        
        // Statistics
        private long _hits = 0;
        private long _misses = 0;

        public DocumentCache(int maxSizeMb = 10, ILogger<DocumentCache>? logger = null)
        {
            // Rough estimate: assume ~10KB per document
            _maxSize = maxSizeMb * 100; // Max number of documents
            _logger = logger ?? NullLogger<DocumentCache>.Instance;
            
            _logger.LogInformation("Initialized document cache with max size {MaxSize} documents", _maxSize);
        }

        /// <summary>
        /// Gets a document from cache.
        /// </summary>
        public Document? Get(string collection, string key)
        {
            lock (_lock)
            {
                var cacheKey = $"{collection}:{key}";
                
                if (_cache.TryGetValue(cacheKey, out var entry))
                {
                    // Move to front (most recently used)
                    _lru.Remove(entry.Node);
                    _lru.AddFirst(entry.Node);
                    
                    _hits++;
                    _logger.LogTrace("Cache hit for {Key}", cacheKey);
                    return entry.Document;
                }
                
                _misses++;
                _logger.LogTrace("Cache miss for {Key}", cacheKey);
                return null;
            }
        }

        /// <summary>
        /// Sets a document in cache.
        /// </summary>
        public void Set(string collection, string key, Document document)
        {
            lock (_lock)
            {
                var cacheKey = $"{collection}:{key}";
                
                // If already exists, update and move to front
                if (_cache.TryGetValue(cacheKey, out var existingEntry))
                {
                    _lru.Remove(existingEntry.Node);
                    var newNode = _lru.AddFirst(cacheKey);
                    _cache[cacheKey] = new CacheEntry(document, newNode);
                    _logger.LogTrace("Updated cache for {Key}", cacheKey);
                    return;
                }
                
                // Evict if full
                if (_cache.Count >= _maxSize)
                {
                    var oldest = _lru.Last!.Value;
                    _lru.RemoveLast();
                    _cache.Remove(oldest);
                    _logger.LogTrace("Evicted oldest cache entry {Key}", oldest);
                }

                var node = _lru.AddFirst(cacheKey);
                _cache[cacheKey] = new CacheEntry(document, node);
                _logger.LogTrace("Added to cache: {Key}", cacheKey);
            }
        }

        /// <summary>
        /// Removes a document from cache.
        /// </summary>
        public void Remove(string collection, string key)
        {
            lock (_lock)
            {
                var cacheKey = $"{collection}:{key}";
                
                if (_cache.TryGetValue(cacheKey, out var entry))
                {
                    _lru.Remove(entry.Node);
                    _cache.Remove(cacheKey);
                    _logger.LogTrace("Removed from cache: {Key}", cacheKey);
                }
            }
        }

        /// <summary>
        /// Clears all cached documents.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                var count = _cache.Count;
                _cache.Clear();
                _lru.Clear();
                _logger.LogInformation("Cleared cache ({Count} entries)", count);
            }
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        public (long Hits, long Misses, int Size, double HitRate) GetStatistics()
        {
            lock (_lock)
            {
                var total = _hits + _misses;
                var hitRate = total > 0 ? (double)_hits / total : 0;
                return (_hits, _misses, _cache.Count, hitRate);
            }
        }
    }
}
