using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

/// <summary>
/// Integration tests to reproduce and verify split-brain and chain linking scenarios.
/// </summary>
public class SplitBrainScenarioTests : IDisposable
{
    private readonly string _nodeAPath;
    private readonly string _nodeBPath;
    private readonly SqlitePeerStore _storeA;
    private readonly SqlitePeerStore _storeB;
    private readonly PeerDatabase _dbA;
    private readonly PeerDatabase _dbB;

    public SplitBrainScenarioTests()
    {
        _nodeAPath = Path.Combine(Path.GetTempPath(), $"test_nodeA_{Guid.NewGuid()}.db");
        _nodeBPath = Path.Combine(Path.GetTempPath(), $"test_nodeB_{Guid.NewGuid()}.db");

        _storeA = new SqlitePeerStore($"Data Source={_nodeAPath}");
        _storeB = new SqlitePeerStore($"Data Source={_nodeBPath}");

        var configA = new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
        {
            NodeId = "nodeA",
            AuthToken = "token",
            TcpPort = 0
        });

        var configB = new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
        {
            NodeId = "nodeB",
            AuthToken = "token",
            TcpPort = 0
        });

        _dbA = new PeerDatabase(_storeA, configA);
        _dbB = new PeerDatabase(_storeB, configB);
    }

    public void Dispose()
    {
        System.Threading.Thread.Sleep(100); // Give SQLite time to release
        try
        {
            if (File.Exists(_nodeAPath)) File.Delete(_nodeAPath);
            if (File.Exists(_nodeBPath)) File.Delete(_nodeBPath);
        }
        catch { /* Ignore */ }
    }

    [Fact]
    public async Task TwoNodes_CreateDocuments_ShouldMaintainSeparateChains()
    {
        // Arrange
        var collectionA = _dbA.Collection("users");
        var collectionB = _dbB.Collection("users");

        // Act
        // Node A creates first document
        await collectionA.Put("user1", new { Name = "Alice" });

        // Simulate sync: Node B gets Node A's data
        var vcA = await _storeA.GetVectorClockAsync();
        var changesFromA = await _storeA.GetOplogForNodeAfterAsync("nodeA", default);
        await _storeB.ApplyBatchAsync(Enumerable.Empty<Document>(), changesFromA);

        // Node B creates its own document  
        await collectionB.Put("user2", new { Name = "Bob" });

        // Simulate sync: Node A gets Node B's data
        var vcB = await _storeB.GetVectorClockAsync();
        var changesFromB = await _storeB.GetOplogForNodeAfterAsync("nodeB", default);

        // Assert - This should NOT throw "Gap Detected" error
        await _storeA.ApplyBatchAsync(Enumerable.Empty<Document>(), changesFromB);

        // Verify both nodes have both documents
        var docA1 = await collectionA.Get<dynamic>("user1");
        var docA2 = await collectionA.Get<dynamic>("user2");
        var docB1 = await collectionB.Get<dynamic>("user1");
        var docB2 = await collectionB.Get<dynamic>("user2");

        Assert.NotNull(docA1);
        Assert.NotNull(docA2);
        Assert.NotNull(docB1);
        Assert.NotNull(docB2);
    }

    [Fact]
    public async Task TwoNodes_AlternatingWrites_ShouldSync()
    {
        // Arrange
        var collectionA = _dbA.Collection("users");
        var collectionB = _dbB.Collection("users");

        // Act
        // Node A writes
        await collectionA.Put("doc1", new { Value = "A1" });

        // Sync A?B
        var changesA1 = await _storeA.GetOplogForNodeAfterAsync("nodeA", default);
        await _storeB.ApplyBatchAsync(Enumerable.Empty<Document>(), changesA1);

        // Node B writes
        await collectionB.Put("doc2", new { Value = "B1" });

        // Node A writes again
        await collectionA.Put("doc3", new { Value = "A2" });

        // Sync A?B
        var vcB = await _storeB.GetVectorClockAsync();
        var nodeATs = vcB.GetTimestamp("nodeA");
        var changesA2 = await _storeA.GetOplogForNodeAfterAsync("nodeA", nodeATs);
        await _storeB.ApplyBatchAsync(Enumerable.Empty<Document>(), changesA2);

        // Sync B?A (This is where the bug might occur)
        var vcA = await _storeA.GetVectorClockAsync();
        var nodeBTs = vcA.GetTimestamp("nodeB");
        var changesB1 = await _storeB.GetOplogForNodeAfterAsync("nodeB", nodeBTs);
        
        // Assert - Should not throw
        await _storeA.ApplyBatchAsync(Enumerable.Empty<Document>(), changesB1);

        // Verify
        Assert.NotNull(await collectionA.Get<dynamic>("doc1"));
        Assert.NotNull(await collectionA.Get<dynamic>("doc2"));
        Assert.NotNull(await collectionA.Get<dynamic>("doc3"));
    }
}

/// <summary>
/// Static configuration provider for testing.
/// </summary>
internal class StaticPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
{
    private readonly PeerNodeConfiguration _config;

    public StaticPeerNodeConfigurationProvider(PeerNodeConfiguration config)
    {
        _config = config;
    }

    public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

    public Task<PeerNodeConfiguration> GetConfiguration() => Task.FromResult(_config);
}
