using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace EntglDb.Sample.Shared.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SampleDbContext _context;
    private readonly SampleDocumentStore _documentStore;
    private readonly BLiteOplogStore<SampleDbContext> _oplogStore;
    private readonly BLitePeerConfigurationStore<SampleDbContext> _peerConfigStore;
    private readonly SnapshotStore _snapshotStore;
    private readonly TestPeerNodeConfigurationProvider _configProvider;

    public SnapshotStoreTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test-snapshot-{Guid.NewGuid()}.blite");
        _context = new SampleDbContext(_testDbPath);
        _configProvider = new TestPeerNodeConfigurationProvider("test-node");
        
        _documentStore = new SampleDocumentStore(_context, _configProvider, NullLogger<SampleDocumentStore>.Instance);
        var snapshotMetadataStore = new BLiteSnapshotMetadataStore<SampleDbContext>(
            _context,
            NullLogger<BLiteSnapshotMetadataStore<SampleDbContext>>.Instance);
        _oplogStore = new BLiteOplogStore<SampleDbContext>(
            _context, 
            _documentStore, 
            new LastWriteWinsConflictResolver(),
            snapshotMetadataStore,
            NullLogger<BLiteOplogStore<SampleDbContext>>.Instance);
        _peerConfigStore = new BLitePeerConfigurationStore<SampleDbContext>(
            _context,
            NullLogger<BLitePeerConfigurationStore<SampleDbContext>>.Instance);
        
        _snapshotStore = new SnapshotStore(
            _documentStore,
            _peerConfigStore,
            _oplogStore,
            new LastWriteWinsConflictResolver(),
            NullLogger<SnapshotStore>.Instance);
    }


    [Fact]
    public async Task CreateSnapshotAsync_WritesValidJsonToStream()
    {
        // Arrange - Add some data
        var user = new User { Id = "user-1", Name = "Alice", Age = 30 };
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();

        // Act - Create snapshot
        using var stream = new MemoryStream();
        await _snapshotStore.CreateSnapshotAsync(stream);
        
        // Assert - Stream should contain valid JSON
        Assert.True(stream.Length > 0, "Snapshot stream should not be empty");
        
        // Reset stream position and verify JSON is valid
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();
        
        Assert.False(string.IsNullOrWhiteSpace(json), "Snapshot JSON should not be empty");
        Assert.StartsWith("{", json.Trim(), StringComparison.Ordinal);
        
        // Verify it's valid JSON by parsing
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
        
        // Verify structure
        Assert.True(doc.RootElement.TryGetProperty("Version", out _), "Should have Version property");
        Assert.True(doc.RootElement.TryGetProperty("Documents", out _), "Should have Documents property");
        Assert.True(doc.RootElement.TryGetProperty("Oplog", out _), "Should have Oplog property");
    }

    [Fact]
    public async Task CreateSnapshotAsync_IncludesAllDocuments()
    {
        // Arrange - Add multiple documents
        await _context.Users.InsertAsync(new User { Id = "u1", Name = "User 1", Age = 20 });
        await _context.Users.InsertAsync(new User { Id = "u2", Name = "User 2", Age = 25 });
        await _context.TodoLists.InsertAsync(new TodoList 
        { 
            Id = "t1", 
            Name = "My List",
            Items = [new TodoItem { Task = "Task 1", Completed = false }]
        });
        await _context.SaveChangesAsync();

        // Act
        using var stream = new MemoryStream();
        await _snapshotStore.CreateSnapshotAsync(stream);
        
        // Assert
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        
        var documents = doc.RootElement.GetProperty("Documents");
        Assert.Equal(3, documents.GetArrayLength());
    }

    [Fact]
    public async Task RoundTrip_CreateAndReplace_PreservesData()
    {
        // Arrange - Add data to source
        var originalUser = new User { Id = "user-rt", Name = "RoundTrip User", Age = 42 };
        await _context.Users.InsertAsync(originalUser);
        await _context.SaveChangesAsync();

        // Create snapshot
        using var snapshotStream = new MemoryStream();
        await _snapshotStore.CreateSnapshotAsync(snapshotStream);
        snapshotStream.Position = 0;

        // Create a new context/stores (simulating a different node)
        var newDbPath = Path.Combine(Path.GetTempPath(), $"test-snapshot-target-{Guid.NewGuid()}.blite");
        try
        {
            using var newContext = new SampleDbContext(newDbPath);
            var newConfigProvider = new TestPeerNodeConfigurationProvider("test-new-node");
            var newDocStore = new SampleDocumentStore(newContext, newConfigProvider, NullLogger<SampleDocumentStore>.Instance);
            var newSnapshotMetaStore = new BLiteSnapshotMetadataStore<SampleDbContext>(
                newContext, NullLogger<BLiteSnapshotMetadataStore<SampleDbContext>>.Instance);
            var newOplogStore = new BLiteOplogStore<SampleDbContext>(
                newContext, newDocStore, new LastWriteWinsConflictResolver(),
                newSnapshotMetaStore,
                NullLogger<BLiteOplogStore<SampleDbContext>>.Instance);
            var newPeerStore = new BLitePeerConfigurationStore<SampleDbContext>(
                newContext, NullLogger<BLitePeerConfigurationStore<SampleDbContext>>.Instance);
            
            var newSnapshotStore = new SnapshotStore(
                newDocStore, newPeerStore, newOplogStore, new LastWriteWinsConflictResolver(),
                NullLogger<SnapshotStore>.Instance);

            // Act - Replace database with snapshot
            await newSnapshotStore.ReplaceDatabaseAsync(snapshotStream);

            // Assert - Data should be restored
            var restoredUser = newContext.Users.FindById("user-rt");
            Assert.NotNull(restoredUser);
            Assert.Equal("RoundTrip User", restoredUser.Name);
            Assert.Equal(42, restoredUser.Age);
        }
        finally
        {
            if (File.Exists(newDbPath))
                try { File.Delete(newDbPath); } catch { }
        }
    }

    [Fact]
    public async Task MergeSnapshotAsync_MergesWithExistingData()
    {
        // Arrange - Add initial data
        await _context.Users.InsertAsync(new User { Id = "existing", Name = "Existing User", Age = 30 });
        await _context.SaveChangesAsync();

        // Create snapshot with different data
        var sourceDbPath = Path.Combine(Path.GetTempPath(), $"test-snapshot-source-{Guid.NewGuid()}.blite");
        MemoryStream snapshotStream;
        
        try
        {
            using var sourceContext = new SampleDbContext(sourceDbPath);
            await sourceContext.Users.InsertAsync(new User { Id = "new-user", Name = "New User", Age = 25 });
            await sourceContext.SaveChangesAsync();

            var sourceConfigProvider = new TestPeerNodeConfigurationProvider("test-source-node");
            var sourceDocStore = new SampleDocumentStore(sourceContext, sourceConfigProvider, NullLogger<SampleDocumentStore>.Instance);
            var sourceSnapshotMetaStore = new BLiteSnapshotMetadataStore<SampleDbContext>(
                sourceContext, NullLogger<BLiteSnapshotMetadataStore<SampleDbContext>>.Instance);
            var sourceOplogStore = new BLiteOplogStore<SampleDbContext>(
                sourceContext, sourceDocStore, new LastWriteWinsConflictResolver(),
                sourceSnapshotMetaStore,
                NullLogger<BLiteOplogStore<SampleDbContext>>.Instance);
            var sourcePeerStore = new BLitePeerConfigurationStore<SampleDbContext>(
                sourceContext, NullLogger<BLitePeerConfigurationStore<SampleDbContext>>.Instance);
            
            var sourceSnapshotStore = new SnapshotStore(
                sourceDocStore, sourcePeerStore, sourceOplogStore, new LastWriteWinsConflictResolver(),
                NullLogger<SnapshotStore>.Instance);

            snapshotStream = new MemoryStream();
            await sourceSnapshotStore.CreateSnapshotAsync(snapshotStream);
            snapshotStream.Position = 0;
        }
        finally
        {
            if (File.Exists(sourceDbPath))
                try { File.Delete(sourceDbPath); } catch { }
        }

        // Act - Merge snapshot into existing data
        await _snapshotStore.MergeSnapshotAsync(snapshotStream);

        // Assert - Both users should exist
        var existingUser = _context.Users.FindById("existing");
        var newUser = _context.Users.FindById("new-user");
        
        Assert.NotNull(existingUser);
        Assert.NotNull(newUser);
        Assert.Equal("Existing User", existingUser.Name);
        Assert.Equal("New User", newUser.Name);
    }

    [Fact]
    public async Task CreateSnapshotAsync_HandlesEmptyDatabase()
    {
        // Act - Create snapshot from empty database
        using var stream = new MemoryStream();
        await _snapshotStore.CreateSnapshotAsync(stream);
        
        // Assert - Should still produce valid JSON
        Assert.True(stream.Length > 0);
        
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        
        var documents = doc.RootElement.GetProperty("Documents");
        Assert.Equal(0, documents.GetArrayLength());
    }

    [Fact]
    public async Task CreateSnapshotAsync_IncludesOplogEntries()
    {
        // Arrange - Create some oplog entries via document changes
        await _context.Users.InsertAsync(new User { Id = "op-user", Name = "Oplog User", Age = 20 });
        await _context.SaveChangesAsync();
        
        // Manually add an oplog entry to ensure it's captured
        var oplogEntry = new OplogEntry(
            "Users",
            "manual-key",
            OperationType.Put,
            JsonDocument.Parse("{\"test\": true}").RootElement,
            new HlcTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, "test-node"),
            ""
        );
        await _oplogStore.AppendOplogEntryAsync(oplogEntry);

        // Act
        using var stream = new MemoryStream();
        await _snapshotStore.CreateSnapshotAsync(stream);
        
        // Assert
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        
        var oplog = doc.RootElement.GetProperty("Oplog");
        Assert.True(oplog.GetArrayLength() >= 1, "Should have at least one oplog entry");
    }

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
