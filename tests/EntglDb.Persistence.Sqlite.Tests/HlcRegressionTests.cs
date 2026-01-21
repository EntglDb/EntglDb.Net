using EntglDb.Core;
using EntglDb.Core.Network;
using FluentAssertions;
using System.Data;
using System.Text.Json;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class HlcRegressionTests : IDisposable
{
    private readonly string _dbPath;

    public HlcRegressionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hlc-regression-{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private class StubNodeConfigProvider : IPeerNodeConfigurationProvider
    {
        public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
        public Task<PeerNodeConfiguration> GetConfiguration()
        {
            return Task.FromResult(new PeerNodeConfiguration { NodeId = "test-node", AuthToken = "token", TcpPort = 5000 });
        }
    }

    [Fact]
    public async Task GetLatestTimestampAsync_ShouldFindTimestamp_AfterRestart_WithPerCollectionTables()
    {
        // 1. Arrange: Initialize store and write some data to create tables and oplog
        var configProvider = new StubNodeConfigProvider();
        var options = new SqlitePersistenceOptions 
        { 
            BasePath = Path.GetDirectoryName(_dbPath),
            DatabaseFilenameTemplate = Path.GetFileName(_dbPath),
            UsePerCollectionTables = true 
        };

        var store1 = new SqlitePeerStore(configProvider, options);
        var doc = new Document("test_collection", "key1", JsonDocument.Parse("{}").RootElement, new HlcTimestamp(1000, 0, "test-node"), false);
        var oplog = new OplogEntry("test_collection", "key1", OperationType.Put, null, new HlcTimestamp(1000, 0, "test-node"));
        
        await store1.SaveDocumentAsync(doc);
        await store1.AppendOplogEntryAsync(oplog); // Ensure oplog table exists and has data
        
        // 2. Simulate restart by creating a NEW store instance. 
        // The new instance has an empty _createdTables cache.
        var store2 = new SqlitePeerStore(configProvider, options);

        // 3. Act: Get timestamp
        // Before the fix, this would return (0,0) because it wouldn't know about "test_collection" oplog table
        var timestamp = await store2.GetLatestTimestampAsync();

        // 4. Assert
        timestamp.PhysicalTime.Should().Be(1000);
        timestamp.NodeId.Should().Be("test-node");
    }
}
