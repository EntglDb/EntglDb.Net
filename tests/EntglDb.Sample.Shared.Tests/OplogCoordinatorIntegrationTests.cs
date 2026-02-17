using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Sample.Shared.Tests;

public class OplogCoordinatorIntegrationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SampleDbContext _context;
    private readonly SampleDocumentStore _documentStore;
    private readonly BLiteOplogStore<SampleDbContext> _oplogStore;
    private readonly OplogCoordinator _coordinator;
    private readonly TestPeerNodeConfigurationProvider _configProvider;

    public OplogCoordinatorIntegrationTests()
    {
        // Create temporary database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test-oplog-{Guid.NewGuid()}.blite");
        
        // Setup components
        _configProvider = new TestPeerNodeConfigurationProvider("test-node-1");
        _context = new SampleDbContext(_testDbPath);
        
        var conflictResolver = new LastWriteWinsConflictResolver();
        _documentStore = new SampleDocumentStore(_context, conflictResolver, NullLogger<SampleDocumentStore>.Instance);
        
        // Create SnapshotMetadataStore first (needed by OplogStore for VectorClock initialization)
        var snapshotMetadataStore = new BLiteSnapshotMetadataStore<SampleDbContext>(
            _context, NullLogger<BLiteSnapshotMetadataStore<SampleDbContext>>.Instance);
        
        _oplogStore = new BLiteOplogStore<SampleDbContext>(
            _context, 
            _documentStore, 
            conflictResolver,
            snapshotMetadataStore,
            NullLogger<BLiteOplogStore<SampleDbContext>>.Instance);
        
        // Create DocumentMetadataStore for sync tracking
        var documentMetadataStore = new BLiteDocumentMetadataStore<SampleDbContext>(
            _context, NullLogger<BLiteDocumentMetadataStore<SampleDbContext>>.Instance);
        
        // Create OplogCoordinator - it will subscribe to document store events
        _coordinator = new OplogCoordinator(
            _documentStore,
            _oplogStore,
            _configProvider,
            documentMetadataStore,
            NullLogger<OplogCoordinator>.Instance);
    }

    [Fact]
    public async Task InsertDocument_CreatesOplogEntry()
    {
        // Arrange
        var user = new User 
        { 
            Id = "user-1", 
            Name = "Alice", 
            Age = 30,
            Address = new Address { City = "Rome" }
        };

        // Act - Insert through BLite collection
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();

        // Give coordinator time to process event
        await Task.Delay(100);

        // Assert - Check that oplog entry was created
        var oplogEntries = await _oplogStore.ExportAsync();
        var entries = oplogEntries.ToList();
        
        Assert.NotEmpty(entries);
        var entry = entries.First();
        Assert.Equal("Users", entry.Collection);
        Assert.Equal("user-1", entry.Key);
        Assert.Equal(OperationType.Put, entry.Operation);
        Assert.NotNull(entry.Hash);
        Assert.Equal("test-node-1", entry.Timestamp.NodeId);
    }

    [Fact]
    public async Task UpdateDocument_CreatesOplogEntry()
    {
        // Arrange - Insert initial document
        var user = new User { Id = "user-2", Name = "Bob", Age = 25 };
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();
        await Task.Delay(50);

        var initialCount = (await _oplogStore.ExportAsync()).Count();

        // Act - Update the document
        user.Age = 26;
        await _context.Users.UpdateAsync(user);
        await _context.SaveChangesAsync();
        await Task.Delay(100);

        // Assert
        var allEntries = (await _oplogStore.ExportAsync()).ToList();
        Assert.Equal(initialCount + 1, allEntries.Count);
        
        var lastEntry = allEntries.OrderBy(e => e.Timestamp).Last();
        Assert.Equal("Users", lastEntry.Collection);
        Assert.Equal("user-2", lastEntry.Key);
        Assert.Equal(OperationType.Put, lastEntry.Operation);
    }

    [Fact]
    public async Task DeleteDocument_CreatesOplogEntry()
    {
        // Arrange
        var user = new User { Id = "user-3", Name = "Charlie", Age = 35 };
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();
        await Task.Delay(50);

        var initialCount = (await _oplogStore.ExportAsync()).Count();

        // Act - Delete the document
        await _context.Users.DeleteAsync("user-3");
        await _context.SaveChangesAsync();
        await Task.Delay(100);

        // Assert
        var allEntries = (await _oplogStore.ExportAsync()).ToList();
        Assert.Equal(initialCount + 1, allEntries.Count);
        
        var deleteEntry = allEntries.OrderBy(e => e.Timestamp).Last();
        Assert.Equal("Users", deleteEntry.Collection);
        Assert.Equal("user-3", deleteEntry.Key);
        Assert.Equal(OperationType.Delete, deleteEntry.Operation);
        Assert.Null(deleteEntry.Payload);
    }

    [Fact]
    public async Task MultipleOperations_MaintainHashChain()
    {
        // Arrange & Act - Perform multiple operations
        await _context.Users.InsertAsync(new User { Id = "u1", Name = "User 1", Age = 20 });
        await _context.SaveChangesAsync();
        await Task.Delay(50);

        await _context.Users.InsertAsync(new User { Id = "u2", Name = "User 2", Age = 21 });
        await _context.SaveChangesAsync();
        await Task.Delay(50);

        await _context.Users.InsertAsync(new User { Id = "u3", Name = "User 3", Age = 22 });
        await _context.SaveChangesAsync();
        await Task.Delay(100);

        // Assert - Verify hash chain is maintained
        var entries = (await _oplogStore.ExportAsync())
            .Where(e => e.Timestamp.NodeId == "test-node-1")
            .OrderBy(e => e.Timestamp)
            .ToList();

        Assert.True(entries.Count >= 3, "Should have at least 3 entries");

        // Verify first entry has empty previous hash (genesis)
        Assert.Equal(string.Empty, entries[0].PreviousHash);

        // Verify subsequent entries form a valid chain
        for (int i = 1; i < entries.Count; i++)
        {
            Assert.Equal(entries[i - 1].Hash, entries[i].PreviousHash);
        }
    }

    [Fact]
    public async Task TodoList_Operations_CreateOplogEntries()
    {
        // Arrange
        var todoList = new TodoList
        {
            Id = "todo-1",
            Name = "Shopping List",
            Items = new List<TodoItem>
            {
                new TodoItem { Task = "Buy milk", Completed = false },
                new TodoItem { Task = "Buy bread", Completed = true }
            }
        };

        // Act
        await _context.TodoLists.InsertAsync(todoList);
        await _context.SaveChangesAsync();
        await Task.Delay(100);

        // Assert
        var entries = (await _oplogStore.ExportAsync()).ToList();
        var todoEntry = entries.FirstOrDefault(e => e.Collection == "TodoLists" && e.Key == "todo-1");
        
        Assert.NotNull(todoEntry);
        Assert.Equal(OperationType.Put, todoEntry.Operation);
        Assert.NotNull(todoEntry.Payload);
        
        // Verify payload contains the todo items
        var payloadJson = todoEntry.Payload?.GetRawText();
        Assert.NotNull(payloadJson);
        Assert.Contains("Shopping List", payloadJson);
        Assert.Contains("Buy milk", payloadJson);
    }

    [Fact]
    public async Task VectorClock_UpdatesCorrectly()
    {
        // Arrange & Act - Create multiple entries
        for (int i = 0; i < 5; i++)
        {
            await _context.Users.InsertAsync(new User { Id = $"vc-user-{i}", Name = $"User {i}", Age = 20 + i });
            await _context.SaveChangesAsync();
            await Task.Delay(50);
        }

        // Assert - Verify vector clock reflects all changes
        var vectorClock = await _oplogStore.GetVectorClockAsync();
        var timestamp = vectorClock.GetTimestamp("test-node-1");
        
        Assert.NotNull(timestamp);
        Assert.True(timestamp.PhysicalTime > 0);
    }

    public void Dispose()
    {
        _coordinator?.Dispose();
        _context?.Dispose();
        
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
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
