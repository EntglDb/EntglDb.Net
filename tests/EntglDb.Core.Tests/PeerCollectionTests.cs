using EntglDb.Core.Metadata;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace EntglDb.Core.Tests;

public class PeerCollectionTests
{
    // Test entity
    public class TestUser
    {
        [PrimaryKey(AutoGenerate = true)]
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    // In-memory store for testing
    private class InMemoryPeerStore : IPeerStore
    {
        private readonly Dictionary<string, Dictionary<string, Document>> _collections = new();
        private readonly List<OplogEntry> _oplog = new();
        private HlcTimestamp _latestTimestamp = new HlcTimestamp(0, 0, "test");

        public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
        {
            if (!_collections.ContainsKey(document.Collection))
                _collections[document.Collection] = new Dictionary<string, Document>();

            _collections[document.Collection][document.Key] = document;
            return Task.CompletedTask;
        }

        public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (_collections.TryGetValue(collection, out var docs) && docs.TryGetValue(key, out var doc))
                return Task.FromResult<Document?>(doc);
            return Task.FromResult<Document?>(null);
        }

        public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
        {
            _oplog.Add(entry);
            if (entry.Timestamp.CompareTo(_latestTimestamp) > 0)
                _latestTimestamp = entry.Timestamp;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp since, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_oplog.Where(e => e.Timestamp.CompareTo(since) > 0));
        }

        public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_latestTimestamp);
        }

        public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
        {
            var vectorClock = new VectorClock();
            var nodeGroups = _oplog.GroupBy(e => e.Timestamp.NodeId);
            
            foreach (var group in nodeGroups)
            {
                var latest = group.OrderByDescending(e => e.Timestamp).First();
                vectorClock.SetTimestamp(group.Key, latest.Timestamp);
            }
            
            return Task.FromResult(vectorClock);
        }

        public async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default)
        {
            return [.. _oplog
                .Where(e => e.Timestamp.NodeId == nodeId && e.Timestamp.CompareTo(since) > 0)
                .OrderBy(e => e.Timestamp)];
        }

        public Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            var last = _oplog
                .Where(e => e.Timestamp.NodeId == nodeId)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();
            return Task.FromResult(last?.Hash);
        }

        public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
        {
            var entry = _oplog.FirstOrDefault(e => e.Hash == hash);
            return Task.FromResult(entry);
        }

        public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
        {
            // Simplified in-memory implementation
            var start = _oplog.FirstOrDefault(e => e.Hash == startHash);
            var end = _oplog.FirstOrDefault(e => e.Hash == endHash);

            if (start == null || end == null) return Task.FromResult(Enumerable.Empty<OplogEntry>());
            if (start.Timestamp.NodeId != end.Timestamp.NodeId) return Task.FromResult(Enumerable.Empty<OplogEntry>());

            var range = _oplog
                .Where(e => e.Timestamp.NodeId == start.Timestamp.NodeId &&
                            e.Timestamp.CompareTo(start.Timestamp) > 0 &&
                            e.Timestamp.CompareTo(end.Timestamp) <= 0)
                .OrderBy(e => e.Timestamp);

            return Task.FromResult<IEnumerable<OplogEntry>>(range);
        }

        public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
        {
            foreach (var doc in documents)
            {
                if (!_collections.ContainsKey(doc.Collection))
                    _collections[doc.Collection] = new Dictionary<string, Document>();
                _collections[doc.Collection][doc.Key] = doc;
            }

            foreach (var entry in oplogEntries)
            {
                _oplog.Add(entry);
                if (entry.Timestamp.CompareTo(_latestTimestamp) > 0)
                    _latestTimestamp = entry.Timestamp;
            }

            return Task.CompletedTask;
        }

        public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        {
            if (!_collections.TryGetValue(collection, out var docs))
                return Task.FromResult(Enumerable.Empty<Document>());

            var results = docs.Values.AsEnumerable();
            
            if (skip.HasValue)
                results = results.Skip(skip.Value);
            if (take.HasValue)
                results = results.Take(take.Value);

            return Task.FromResult(results);
        }

        public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<string>>(_collections.Keys);
        }

        public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

        public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default)
        {
            // No-op for in-memory store
            return Task.CompletedTask;
        }

        public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default)
        {
             if (!_collections.TryGetValue(collection, out var docs))
                return Task.FromResult(0);

            // In-memory simplistic count (filtering ignored for mock unless needed)
            // If queryExpression is null, count all.
            if (queryExpression == null)
            {
                return Task.FromResult(docs.Count);
            }
            
            // For now return hardcoded 0 or implement rudimentary filter if tests rely on it.
            // Tests use Find(), not Count(). So implementation here might not be exercised fully.
            return Task.FromResult(docs.Count);
        }

        public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
        {
             return Task.CompletedTask;
        }

        public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        // Remote Peer Management
        private readonly List<RemotePeerConfiguration> _remotePeers = new();

        public Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default)
        {
            var existing = _remotePeers.FirstOrDefault(p => p.NodeId == peer.NodeId);
            if (existing != null)
                _remotePeers.Remove(existing);
            _remotePeers.Add(peer);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<RemotePeerConfiguration>>(_remotePeers);
        }

        public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            var peer = _remotePeers.FirstOrDefault(p => p.NodeId == nodeId);
            if (peer != null)
                _remotePeers.Remove(peer);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Put_WithExplicitKey_ShouldStoreDocument()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        var user = new TestUser { Name = "Alice", Age = 30 };

        // Act
        await users.Put("user-1", user);

        // Assert
        var retrieved = await users.Get("user-1");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Alice");
        retrieved.Age.Should().Be(30);
    }

    [Fact]
    public async Task Put_WithoutKey_ShouldAutoGenerateId()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        var user = new TestUser { Name = "Bob", Age = 25 };

        // Act
        await users.Put(user);

        // Assert
        user.Id.Should().NotBeNullOrEmpty();
        user.Id.Should().MatchRegex(@"^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$"); // GUID format
    }

    [Fact]
    public async Task Put_WithoutKey_ShouldBeRetrievableById()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        var user = new TestUser { Name = "Charlie", Age = 35 };

        // Act
        await users.Put(user);
        var retrieved = await users.Get(user.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Charlie");
        retrieved.Age.Should().Be(35);
    }

    [Fact]
    public async Task Get_NonExistingKey_ShouldReturnNull()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        // Act
        var result = await users.Get("non-existing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ShouldRemoveDocument()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        await users.Put("user-1", new TestUser { Name = "Alice", Age = 30 });

        // Act
        await users.Delete("user-1");

        // Assert
        var retrieved = await users.Get("user-1");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task Find_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);
        await db.InitializeAsync();
        var users = db.Collection<TestUser>();

        await users.Put(new TestUser { Name = "Alice", Age = 30 });
        await users.Put(new TestUser { Name = "Bob", Age = 25 });
        await users.Put(new TestUser { Name = "Charlie", Age = 35 });

        // Act
        var results = await users.Find(u => u.Age > 28);

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().Contain(u => u.Name == "Alice");
        results.Should().Contain(u => u.Name == "Charlie");
    }

    [Fact]
    public async Task Collection_ShouldBeCached()
    {
        // Arrange
        var store = new InMemoryPeerStore();
        var config = new StaticPeerNodeConfigurationProvider(new Network.PeerNodeConfiguration()
        {
            NodeId = "test-node",
            TcpPort = 0,
        });
        var db = new PeerDatabase(store, config);

        // Act
        var users1 = db.Collection<TestUser>();
        var users2 = db.Collection<TestUser>();

        // Assert
        users1.Name.Should().Be(users2.Name);
    }
}
