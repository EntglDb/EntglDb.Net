using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class VectorClockPersistenceTests
{
    private readonly string _testDbPath;
    private readonly SqlitePeerStore _store;

    public VectorClockPersistenceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_vc_{Guid.NewGuid()}.db");
        _store = new SqlitePeerStore($"Data Source={_testDbPath}");
    }

    [Fact]
    public async Task GetVectorClockAsync_EmptyStore_ShouldReturnEmptyVectorClock()
    {
        // Act
        var vc = await _store.GetVectorClockAsync();

        // Assert
        Assert.Empty(vc.NodeIds);
    }

    [Fact]
    public async Task GetVectorClockAsync_WithMultipleNodes_ShouldReturnLatestPerNode()
    {
        // Arrange
        var entry1 = new OplogEntry(
            "users", "user1", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(100, 1, "node1"), "", ""
        );

        var entry2 = new OplogEntry(
            "users", "user2", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Bob\"}"),
            new HlcTimestamp(200, 2, "node1"), entry1.Hash, ""
        );

        var entry3 = new OplogEntry(
            "users", "user3", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Charlie\"}"),
            new HlcTimestamp(150, 1, "node2"), "", ""
        );

        await _store.AppendOplogEntryAsync(entry1);
        await _store.AppendOplogEntryAsync(entry2);
        await _store.AppendOplogEntryAsync(entry3);

        // Act
        var vc = await _store.GetVectorClockAsync();

        // Assert
        Assert.Equal(2, vc.NodeIds.Count());
        
        var node1Ts = vc.GetTimestamp("node1");
        Assert.Equal(200, node1Ts.PhysicalTime);
        Assert.Equal(2, node1Ts.LogicalCounter);

        var node2Ts = vc.GetTimestamp("node2");
        Assert.Equal(150, node2Ts.PhysicalTime);
        Assert.Equal(1, node2Ts.LogicalCounter);
    }

    [Fact]
    public async Task GetOplogForNodeAfterAsync_ShouldReturnOnlyEntriesForSpecificNode()
    {
        // Arrange
        var entry1Node1 = new OplogEntry(
            "users", "user1", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(100, 1, "node1"), "", ""
        );

        var entry2Node1 = new OplogEntry(
            "users", "user2", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Bob\"}"),
            new HlcTimestamp(200, 2, "node1"), entry1Node1.Hash, ""
        );

        var entry1Node2 = new OplogEntry(
            "users", "user3", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Charlie\"}"),
            new HlcTimestamp(150, 1, "node2"), "", ""
        );

        await _store.AppendOplogEntryAsync(entry1Node1);
        await _store.AppendOplogEntryAsync(entry2Node1);
        await _store.AppendOplogEntryAsync(entry1Node2);

        // Act - Get entries for node1 after timestamp 100
        var entries = await _store.GetOplogForNodeAfterAsync("node1", new HlcTimestamp(100, 1, "node1"));
        var entriesList = entries.ToList();

        // Assert
        Assert.Single(entriesList);
        Assert.Equal("user2", entriesList[0].Key);
        Assert.Equal(200, entriesList[0].Timestamp.PhysicalTime);
    }

    [Fact]
    public async Task GetOplogForNodeAfterAsync_WithNoMatchingEntries_ShouldReturnEmpty()
    {
        // Arrange
        var entry1 = new OplogEntry(
            "users", "user1", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(100, 1, "node1"), "", ""
        );

        await _store.AppendOplogEntryAsync(entry1);

        // Act - Request entries after the latest timestamp
        var entries = await _store.GetOplogForNodeAfterAsync("node1", new HlcTimestamp(200, 1, "node1"));

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetOplogForNodeAfterAsync_WithNonExistentNode_ShouldReturnEmpty()
    {
        // Arrange
        var entry1 = new OplogEntry(
            "users", "user1", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(100, 1, "node1"), "", ""
        );

        await _store.AppendOplogEntryAsync(entry1);

        // Act
        var entries = await _store.GetOplogForNodeAfterAsync("node999", new HlcTimestamp(0, 0, "node999"));

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetOplogForNodeAfterAsync_ShouldReturnInTimestampOrder()
    {
        // Arrange - Add entries out of order
        var entry2 = new OplogEntry(
            "users", "user2", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Bob\"}"),
            new HlcTimestamp(200, 2, "node1"), "", ""
        );

        var entry1 = new OplogEntry(
            "users", "user1", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
            new HlcTimestamp(100, 1, "node1"), "", ""
        );

        var entry3 = new OplogEntry(
            "users", "user3", OperationType.Put,
            JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Charlie\"}"),
            new HlcTimestamp(150, 1, "node1"), entry1.Hash, ""
        );

        await _store.AppendOplogEntryAsync(entry2);
        await _store.AppendOplogEntryAsync(entry1);
        await _store.AppendOplogEntryAsync(entry3);

        // Act
        var entries = await _store.GetOplogForNodeAfterAsync("node1", new HlcTimestamp(50, 0, "node1"));
        var entriesList = entries.ToList();

        // Assert
        Assert.Equal(3, entriesList.Count);
        Assert.Equal(100, entriesList[0].Timestamp.PhysicalTime);
        Assert.Equal(150, entriesList[1].Timestamp.PhysicalTime);
        Assert.Equal(200, entriesList[2].Timestamp.PhysicalTime);
    }

    [Fact]
    public async Task VectorClockWorkflow_SyncScenario_ShouldWork()
    {
        // Arrange - Simulate two nodes that need to sync
        var node1Path = Path.Combine(Path.GetTempPath(), $"test_node1_{Guid.NewGuid()}.db");
        var node2Path = Path.Combine(Path.GetTempPath(), $"test_node2_{Guid.NewGuid()}.db");

        try
        {
            var node1Store = new SqlitePeerStore($"Data Source={node1Path}");
            var node2Store = new SqlitePeerStore($"Data Source={node2Path}");

            // Node1 has some data
            var entry1 = new OplogEntry(
                "users", "user1", OperationType.Put,
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Alice\"}"),
                new HlcTimestamp(100, 1, "node1"), "", ""
            );
            await node1Store.AppendOplogEntryAsync(entry1);

            // Node2 has different data
            var entry2 = new OplogEntry(
                "users", "user2", OperationType.Put,
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Bob\"}"),
                new HlcTimestamp(150, 1, "node2"), "", ""
            );
            await node2Store.AppendOplogEntryAsync(entry2);

            // Act - Sync process
            var node1VC = await node1Store.GetVectorClockAsync();
            var node2VC = await node2Store.GetVectorClockAsync();

            // Node1 needs to pull from Node2
            var nodesToPull = node1VC.GetNodesWithUpdates(node2VC).ToList();
            Assert.Single(nodesToPull);
            Assert.Contains("node2", nodesToPull);

            // Node1 pulls node2's data
            var changesToPull = await node2Store.GetOplogForNodeAfterAsync("node2", node1VC.GetTimestamp("node2"));
            await node1Store.ApplyBatchAsync(Enumerable.Empty<Document>(), changesToPull);

            // Node2 needs to pull from Node1
            var nodesToPush = node1VC.GetNodesToPush(node2VC).ToList();
            Assert.Single(nodesToPush);
            Assert.Contains("node1", nodesToPush);

            // Node2 pulls node1's data
            var changesToPush = await node1Store.GetOplogForNodeAfterAsync("node1", node2VC.GetTimestamp("node1"));
            await node2Store.ApplyBatchAsync(Enumerable.Empty<Document>(), changesToPush);

            // Assert - Both nodes should have same vector clock now
            var finalNode1VC = await node1Store.GetVectorClockAsync();
            var finalNode2VC = await node2Store.GetVectorClockAsync();

            Assert.Equal(CausalityRelation.Equal, finalNode1VC.CompareTo(finalNode2VC));
        }
        finally
        {
            // Give some time for SQLite to release the files
            System.Threading.Thread.Sleep(100);
            try
            {
                if (File.Exists(node1Path)) File.Delete(node1Path);
                if (File.Exists(node2Path)) File.Delete(node2Path);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
