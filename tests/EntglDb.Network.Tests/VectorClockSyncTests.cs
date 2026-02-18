using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Network.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EntglDb.Network.Tests;

public class VectorClockSyncTests
{
    [Fact]
    public async Task VectorClockSync_ShouldPullOnlyNodesWithUpdates()
    {
        // Arrange
        var localStore = new MockPeerStore();
        var remoteStore = new MockPeerStore();

        // Local knows about node1 and node2
        localStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        localStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));

        // Remote has updates for node1 only
        remoteStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(200, 5, "node1"));
        remoteStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));

        // Add oplog entries for node1 in remote
        remoteStore.OplogEntries.Add(new OplogEntry(
            "users", "user1", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(150, 2, "node1"), "", "hash1"
        ));
        remoteStore.OplogEntries.Add(new OplogEntry(
            "users", "user2", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"Bob\"}"),
            new HlcTimestamp(200, 5, "node1"), "hash1", "hash2"
        ));

        // Act
        var localVC = await localStore.GetVectorClockAsync(default);
        var remoteVC = remoteStore.VectorClock;

        var nodesToPull = localVC.GetNodesWithUpdates(remoteVC).ToList();

        // Assert
        Assert.Single(nodesToPull);
        Assert.Contains("node1", nodesToPull);

        // Simulate pull
        foreach (var nodeId in nodesToPull)
        {
            var localTs = localVC.GetTimestamp(nodeId);
            var changes = await remoteStore.GetOplogForNodeAfterAsync(nodeId, localTs, default);
            
            Assert.Equal(2, changes.Count());
        }
    }

    [Fact]
    public async Task VectorClockSync_ShouldPushOnlyNodesWithLocalUpdates()
    {
        // Arrange
        var localStore = new MockPeerStore();
        var remoteStore = new MockPeerStore();

        // Local has updates for node1
        localStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(200, 5, "node1"));
        localStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));

        // Remote is behind on node1
        remoteStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        remoteStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));

        // Add oplog entries for node1 in local
        localStore.OplogEntries.Add(new OplogEntry(
            "users", "user1", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"Charlie\"}"),
            new HlcTimestamp(150, 2, "node1"), "", "hash1"
        ));

        // Act
        var localVC = localStore.VectorClock;
        var remoteVC = remoteStore.VectorClock;

        var nodesToPush = localVC.GetNodesToPush(remoteVC).ToList();

        // Assert
        Assert.Single(nodesToPush);
        Assert.Contains("node1", nodesToPush);

        // Simulate push
        foreach (var nodeId in nodesToPush)
        {
            var remoteTs = remoteVC.GetTimestamp(nodeId);
            var changes = await localStore.GetOplogForNodeAfterAsync(nodeId, remoteTs, default);
            
            Assert.Single(changes);
        }
    }

    [Fact]
    public async Task VectorClockSync_SplitBrain_ShouldSyncBothDirections()
    {
        // Arrange - Simulating split brain
        var partition1Store = new MockPeerStore();
        var partition2Store = new MockPeerStore();

        // Partition 1 has node1 and node2 updates
        partition1Store.VectorClock.SetTimestamp("node1", new HlcTimestamp(300, 5, "node1"));
        partition1Store.VectorClock.SetTimestamp("node2", new HlcTimestamp(200, 3, "node2"));
        partition1Store.VectorClock.SetTimestamp("node3", new HlcTimestamp(50, 1, "node3"));

        // Partition 2 has node3 updates
        partition2Store.VectorClock.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        partition2Store.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));
        partition2Store.VectorClock.SetTimestamp("node3", new HlcTimestamp(400, 8, "node3"));

        partition1Store.OplogEntries.Add(new OplogEntry(
            "users", "user1", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"P1User\"}"),
            new HlcTimestamp(300, 5, "node1"), "", "hash_p1"
        ));

        partition2Store.OplogEntries.Add(new OplogEntry(
            "users", "user2", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"P2User\"}"),
            new HlcTimestamp(400, 8, "node3"), "", "hash_p2"
        ));

        // Act
        var vc1 = partition1Store.VectorClock;
        var vc2 = partition2Store.VectorClock;

        var relation = vc1.CompareTo(vc2);
        var partition1NeedsToPull = vc1.GetNodesWithUpdates(vc2).ToList();
        var partition1NeedsToPush = vc1.GetNodesToPush(vc2).ToList();

        // Assert
        Assert.Equal(CausalityRelation.Concurrent, relation);

        // Partition 1 needs to pull node3
        Assert.Single(partition1NeedsToPull);
        Assert.Contains("node3", partition1NeedsToPull);

        // Partition 1 needs to push node1 and node2
        Assert.Equal(2, partition1NeedsToPush.Count);
        Assert.Contains("node1", partition1NeedsToPush);
        Assert.Contains("node2", partition1NeedsToPush);

        // Verify data can be synced
        var changesToPull = await partition2Store.GetOplogForNodeAfterAsync("node3", vc1.GetTimestamp("node3"), default);
        Assert.Single(changesToPull);

        var changesToPush = await partition1Store.GetOplogForNodeAfterAsync("node1", vc2.GetTimestamp("node1"), default);
        Assert.Single(changesToPush);
    }

    [Fact]
    public void VectorClockSync_EqualClocks_ShouldNotSync()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc1.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var relation = vc1.CompareTo(vc2);
        var nodesToPull = vc1.GetNodesWithUpdates(vc2).ToList();
        var nodesToPush = vc1.GetNodesToPush(vc2).ToList();

        // Assert
        Assert.Equal(CausalityRelation.Equal, relation);
        Assert.Empty(nodesToPull);
        Assert.Empty(nodesToPush);
    }

    [Fact]
    public async Task VectorClockSync_NewNodeJoins_ShouldBeDetected()
    {
        // Arrange - Simulating a new node joining the cluster
        var existingNodeStore = new MockPeerStore();
        existingNodeStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        existingNodeStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));

        var newNodeStore = new MockPeerStore();
        newNodeStore.VectorClock.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        newNodeStore.VectorClock.SetTimestamp("node2", new HlcTimestamp(100, 1, "node2"));
        newNodeStore.VectorClock.SetTimestamp("node3", new HlcTimestamp(50, 1, "node3")); // New node

        newNodeStore.OplogEntries.Add(new OplogEntry(
            "users", "user3", OperationType.Put,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"name\":\"NewNode\"}"),
            new HlcTimestamp(50, 1, "node3"), "", "hash_new"
        ));

        // Act
        var existingVC = existingNodeStore.VectorClock;
        var newNodeVC = newNodeStore.VectorClock;

        var nodesToPull = existingVC.GetNodesWithUpdates(newNodeVC).ToList();

        // Assert
        Assert.Single(nodesToPull);
        Assert.Contains("node3", nodesToPull);

        var changes = await newNodeStore.GetOplogForNodeAfterAsync("node3", existingVC.GetTimestamp("node3"), default);
        Assert.Single(changes);
    }

    // Mock store for testing
    private class MockPeerStore : IOplogStore
    {
        public VectorClock VectorClock { get; } = new VectorClock();
        public List<OplogEntry> OplogEntries { get; } = new List<OplogEntry>();

        public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

        public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VectorClock);
        }

        public Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
        {
            var query = OplogEntries
                .Where(e => e.Timestamp.NodeId == nodeId && e.Timestamp.CompareTo(since) > 0);
            
            if (collections != null && collections.Any())
            {
                query = query.Where(e => collections.Contains(e.Collection));
            }
            
            return Task.FromResult<IEnumerable<OplogEntry>>(query.OrderBy(e => e.Timestamp).ToList());
        }

        // Minimal stub implementations
        public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BackupAsync(string backupPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<OplogEntry>());
        public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<string>());
        public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
        public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<OplogEntry?>(null);
        public Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, "test"));
        public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
        {
            var query = OplogEntries.Where(e => e.Timestamp.CompareTo(timestamp) > 0);
            
            if (collections != null && collections.Any())
            {
                query = query.Where(e => collections.Contains(e.Collection));
            }
            
            return Task.FromResult<IEnumerable<OplogEntry>>(query.ToList());
        }
        public Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<RemotePeerConfiguration>());
        public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<Document>());
        public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task ClearAllDataAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DropAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
