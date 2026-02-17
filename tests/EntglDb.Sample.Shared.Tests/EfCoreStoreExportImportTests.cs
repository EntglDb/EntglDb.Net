using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace EntglDb.Sample.Shared.Tests;

/// <summary>
/// Tests for EF Core persistence stores: Export, Import, Merge, Drop operations.
/// </summary>
/// <remarks>
/// SKIPPED: These tests are temporarily disabled due to EfCoreOplogStore initialization issue.
/// The InitializeNodeCache method tries to query OplogEntity before the DbContext is fully initialized.
/// This needs investigation in the EfCore persistence layer.
/// </remarks>
public class EfCoreStoreExportImportTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SampleEfCoreDbContext _context;
    private readonly SampleEfCoreDocumentStore _documentStore;
    private readonly EfCoreOplogStore<SampleEfCoreDbContext> _oplogStore;
    private readonly EfCorePeerConfigurationStore<SampleEfCoreDbContext> _peerConfigStore;
    private readonly EfCoreSnapshotMetadaStore<SampleEfCoreDbContext> _snapshotMetadataStore;
    private readonly TestPeerNodeConfigurationProvider _configProvider;

    public EfCoreStoreExportImportTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test-efcore-export-import-{Guid.NewGuid()}.db");
        _context = new SampleEfCoreDbContext(_testDbPath);

        // IMPORTANT: Create database schema FIRST before instantiating stores
        _context.Database.EnsureCreated();

        _configProvider = new TestPeerNodeConfigurationProvider("test-node");

        _documentStore = new SampleEfCoreDocumentStore(_context, _configProvider, NullLogger<SampleEfCoreDocumentStore>.Instance);

        // Create SnapshotMetadataStore first (needed by OplogStore)
        _snapshotMetadataStore = new EfCoreSnapshotMetadaStore<SampleEfCoreDbContext>(
            _context, NullLogger<EfCoreSnapshotMetadaStore<SampleEfCoreDbContext>>.Instance);

        // OplogStore requires: context, documentStore, snapshotMetadataStore, conflictResolver, logger
        _oplogStore = new EfCoreOplogStore<SampleEfCoreDbContext>(
            _context,
            _documentStore,
            _snapshotMetadataStore,
            new LastWriteWinsConflictResolver(),
            NullLogger<EfCoreOplogStore<SampleEfCoreDbContext>>.Instance);

        _peerConfigStore = new EfCorePeerConfigurationStore<SampleEfCoreDbContext>(
            _context, NullLogger<EfCorePeerConfigurationStore<SampleEfCoreDbContext>>.Instance);
    }

    #region OplogStore Tests

    [Fact]
    public async Task OplogStore_ExportAsync_ReturnsAllEntries()
    {
        // Arrange
        var entry1 = CreateOplogEntry("col1", "key1", "node1", 1000);
        var entry2 = CreateOplogEntry("col2", "key2", "node1", 2000);
        await _oplogStore.AppendOplogEntryAsync(entry1);
        await _oplogStore.AppendOplogEntryAsync(entry2);

        // Act
        var exported = (await _oplogStore.ExportAsync()).ToList();

        // Assert
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, e => e.Key == "key1");
        Assert.Contains(exported, e => e.Key == "key2");
    }

    [Fact]
    public async Task OplogStore_ImportAsync_AddsEntries()
    {
        // Arrange
        var entries = new[]
        {
            CreateOplogEntry("col1", "imported1", "node1", 1000),
            CreateOplogEntry("col2", "imported2", "node1", 2000)
        };

        // Act
        await _oplogStore.ImportAsync(entries);

        // Assert
        var exported = (await _oplogStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, e => e.Key == "imported1");
        Assert.Contains(exported, e => e.Key == "imported2");
    }

    [Fact]
    public async Task OplogStore_MergeAsync_OnlyAddsNewEntries()
    {
        // Arrange - Add existing entry
        var existing = CreateOplogEntry("col1", "existing", "node1", 1000);
        await _oplogStore.AppendOplogEntryAsync(existing);

        // Create entries to merge (one duplicate hash, one new)
        var toMerge = new[]
        {
            existing, // Same hash - should be skipped
            CreateOplogEntry("col2", "new-entry", "node1", 2000)
        };

        // Act
        await _oplogStore.MergeAsync(toMerge);

        // Assert
        var exported = (await _oplogStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count); // existing + new, not 3
    }

    [Fact]
    public async Task OplogStore_DropAsync_ClearsAllEntries()
    {
        // Arrange
        await _oplogStore.AppendOplogEntryAsync(CreateOplogEntry("col1", "key1", "node1", 1000));
        await _oplogStore.AppendOplogEntryAsync(CreateOplogEntry("col2", "key2", "node1", 2000));

        // Act
        await _oplogStore.DropAsync();

        // Assert
        var exported = (await _oplogStore.ExportAsync()).ToList();
        Assert.Empty(exported);
    }

    #endregion

    #region PeerConfigurationStore Tests

    [Fact]
    public async Task PeerConfigStore_ExportAsync_ReturnsAllPeers()
    {
        // Arrange
        await _peerConfigStore.SaveRemotePeerAsync(CreatePeerConfig("peer1", "host1:5000"));
        await _peerConfigStore.SaveRemotePeerAsync(CreatePeerConfig("peer2", "host2:5000"));

        // Act
        var exported = (await _peerConfigStore.ExportAsync()).ToList();

        // Assert
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, p => p.NodeId == "peer1");
        Assert.Contains(exported, p => p.NodeId == "peer2");
    }

    [Fact]
    public async Task PeerConfigStore_ImportAsync_AddsPeers()
    {
        // Arrange
        var peers = new[]
        {
            CreatePeerConfig("imported1", "host1:5000"),
            CreatePeerConfig("imported2", "host2:5000")
        };

        // Act
        await _peerConfigStore.ImportAsync(peers);

        // Assert
        var exported = (await _peerConfigStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public async Task PeerConfigStore_MergeAsync_OnlyAddsNewPeers()
    {
        // Arrange - Add existing peer
        var existing = CreatePeerConfig("existing-peer", "host1:5000");
        await _peerConfigStore.SaveRemotePeerAsync(existing);

        var toMerge = new[]
        {
            CreatePeerConfig("existing-peer", "host1-updated:5000"), // Same ID - should be skipped
            CreatePeerConfig("new-peer", "host2:5000")
        };

        // Act
        await _peerConfigStore.MergeAsync(toMerge);

        // Assert
        var exported = (await _peerConfigStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public async Task PeerConfigStore_DropAsync_ClearsAllPeers()
    {
        // Arrange
        await _peerConfigStore.SaveRemotePeerAsync(CreatePeerConfig("peer1", "host1:5000"));
        await _peerConfigStore.SaveRemotePeerAsync(CreatePeerConfig("peer2", "host2:5000"));

        // Act
        await _peerConfigStore.DropAsync();

        // Assert
        var exported = (await _peerConfigStore.ExportAsync()).ToList();
        Assert.Empty(exported);
    }

    #endregion

    #region SnapshotMetadataStore Tests

    [Fact]
    public async Task SnapshotMetadataStore_ExportAsync_ReturnsAllMetadata()
    {
        // Arrange
        var meta1 = CreateSnapshotMetadata("node1", 1000);
        var meta2 = CreateSnapshotMetadata("node2", 2000);
        await _snapshotMetadataStore.InsertSnapshotMetadataAsync(meta1);
        await _snapshotMetadataStore.InsertSnapshotMetadataAsync(meta2);

        // Act
        var exported = (await _snapshotMetadataStore.ExportAsync()).ToList();

        // Assert
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, m => m.NodeId == "node1");
        Assert.Contains(exported, m => m.NodeId == "node2");
    }

    [Fact]
    public async Task SnapshotMetadataStore_ImportAsync_AddsMetadata()
    {
        // Arrange
        var metadata = new[]
        {
            CreateSnapshotMetadata("imported1", 1000),
            CreateSnapshotMetadata("imported2", 2000)
        };

        // Act
        await _snapshotMetadataStore.ImportAsync(metadata);

        // Assert
        var exported = (await _snapshotMetadataStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public async Task SnapshotMetadataStore_MergeAsync_OnlyAddsNewMetadata()
    {
        // Arrange - Add existing metadata
        var existing = CreateSnapshotMetadata("existing-node", 1000);
        await _snapshotMetadataStore.InsertSnapshotMetadataAsync(existing);

        var toMerge = new[]
        {
            CreateSnapshotMetadata("existing-node", 9999), // Same NodeId - should be skipped
            CreateSnapshotMetadata("new-node", 2000)
        };

        // Act
        await _snapshotMetadataStore.MergeAsync(toMerge);

        // Assert
        var exported = (await _snapshotMetadataStore.ExportAsync()).ToList();
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public async Task SnapshotMetadataStore_DropAsync_ClearsAllMetadata()
    {
        // Arrange
        await _snapshotMetadataStore.InsertSnapshotMetadataAsync(CreateSnapshotMetadata("node1", 1000));
        await _snapshotMetadataStore.InsertSnapshotMetadataAsync(CreateSnapshotMetadata("node2", 2000));

        // Act
        await _snapshotMetadataStore.DropAsync();

        // Assert
        var exported = (await _snapshotMetadataStore.ExportAsync()).ToList();
        Assert.Empty(exported);
    }

    #endregion

    #region DocumentStore Tests

    [Fact]
    public async Task DocumentStore_ExportAsync_ReturnsAllDocuments()
    {
        // Arrange
        _context.Users.Add(new User { Id = "u1", Name = "User 1", Age = 20 });
        _context.Users.Add(new User { Id = "u2", Name = "User 2", Age = 25 });
        await _context.SaveChangesAsync();

        // Act
        var exported = (await _documentStore.ExportAsync()).ToList();

        // Assert
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, d => d.Key == "u1");
        Assert.Contains(exported, d => d.Key == "u2");
    }

    [Fact]
    public async Task DocumentStore_ImportAsync_AddsDocuments()
    {
        // Arrange
        var docs = new[]
        {
            CreateDocument("Users", "imported1", new User { Id = "imported1", Name = "Imported 1", Age = 30 }),
            CreateDocument("Users", "imported2", new User { Id = "imported2", Name = "Imported 2", Age = 35 })
        };

        // Act
        await _documentStore.ImportAsync(docs);

        // Assert
        var u1 = await _context.Users.FindAsync("imported1");
        var u2 = await _context.Users.FindAsync("imported2");
        Assert.NotNull(u1);
        Assert.NotNull(u2);
        Assert.Equal("Imported 1", u1.Name);
        Assert.Equal("Imported 2", u2.Name);
    }

    [Fact]
    public async Task DocumentStore_MergeAsync_UsesConflictResolution()
    {
        // Arrange - Add existing document
        _context.Users.Add(new User { Id = "merge-user", Name = "Original", Age = 20 });
        await _context.SaveChangesAsync();

        // Create document to merge with newer timestamp
        var newerDoc = new Document(
            "Users",
            "merge-user",
            JsonDocument.Parse("{\"Id\":\"merge-user\",\"Name\":\"Updated\",\"Age\":25}").RootElement,
            new HlcTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000, 0, "other-node"),
            false
        );

        // Act
        await _documentStore.MergeAsync([newerDoc]);

        // Assert - With LastWriteWins, newer document should win
        var user = await _context.Users.FindAsync("merge-user");
        Assert.NotNull(user);
        Assert.Equal("Updated", user.Name);
        Assert.Equal(25, user.Age);
    }

    [Fact]
    public async Task DocumentStore_DropAsync_ClearsAllDocuments()
    {
        // Arrange
        _context.Users.Add(new User { Id = "drop1", Name = "User 1", Age = 20 });
        _context.Users.Add(new User { Id = "drop2", Name = "User 2", Age = 25 });
        await _context.SaveChangesAsync();

        // Act
        await _documentStore.DropAsync();

        // Assert
        var exported = (await _documentStore.ExportAsync()).ToList();
        Assert.Empty(exported);
    }

    #endregion

    #region Helpers

    private static OplogEntry CreateOplogEntry(string collection, string key, string nodeId, long physicalTime)
    {
        var payload = JsonDocument.Parse($"{{\"test\": \"{key}\"}}").RootElement;
        var timestamp = new HlcTimestamp(physicalTime, 0, nodeId);
        return new OplogEntry(collection, key, OperationType.Put, payload, timestamp, "");
    }

    private static RemotePeerConfiguration CreatePeerConfig(string nodeId, string address)
    {
        return new RemotePeerConfiguration
        {
            NodeId = nodeId,
            Address = address,
            Type = PeerType.StaticRemote,
            IsEnabled = true,
            InterestingCollections = new List<string> { "Users" }
        };
    }

    private static SnapshotMetadata CreateSnapshotMetadata(string nodeId, long physicalTime)
    {
        return new SnapshotMetadata
        {
            NodeId = nodeId,
            TimestampPhysicalTime = physicalTime,
            TimestampLogicalCounter = 0,
            Hash = $"hash-{nodeId}"
        };
    }

    private static Document CreateDocument<T>(string collection, string key, T entity) where T : class
    {
        var json = JsonSerializer.Serialize(entity);
        var content = JsonDocument.Parse(json).RootElement;
        return new Document(collection, key, content, new HlcTimestamp(0, 0, ""), false);
    }

    #endregion

    public void Dispose()
    {
        _documentStore?.Dispose();
        _context?.Dispose();

        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    // Helper class for testing
    private class TestPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
    {
        private readonly PeerNodeConfiguration _config;

        public TestPeerNodeConfigurationProvider(string nodeId)
        {
            _config = new PeerNodeConfiguration
            {
                NodeId = nodeId,
                TcpPort = 5000,
                AuthToken = "test-token",
                InterestingCollections = new List<string> { "Users", "TodoLists" },
                OplogRetentionHours = 24,
                MaintenanceIntervalMinutes = 60
            };
        }

        public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

        public Task<PeerNodeConfiguration> GetConfiguration()
        {
            return Task.FromResult(_config);
        }
    }
}
