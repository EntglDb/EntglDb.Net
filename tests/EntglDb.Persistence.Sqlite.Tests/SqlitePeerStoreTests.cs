using EntglDb.Core;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class SqlitePeerStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqlitePeerStore _store;

    public SqlitePeerStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.db");
        _store = new SqlitePeerStore($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        // SqlitePeerStore doesn't implement IDisposable, just delete the file
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private static Document CreateDocument(string collection, string key, object data, HlcTimestamp timestamp, bool isDeleted = false)
    {
        var json = JsonSerializer.Serialize(data);
        var jsonElement = JsonDocument.Parse(json).RootElement;
        return new Document(collection, key, jsonElement, timestamp, isDeleted);
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldPersistDocument()
    {
        // Arrange
        var doc = CreateDocument("users", "user1", new { Name = "Alice", Age = 30 }, new HlcTimestamp(1000, 0, "test-node"));

        // Act
        await _store.SaveDocumentAsync(doc);

        // Assert
        var retrieved = await _store.GetDocumentAsync("users", "user1");
        retrieved.Should().NotBeNull();
        retrieved!.Key.Should().Be("user1");
        retrieved.Content.GetProperty("Name").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task GetDocumentAsync_ShouldReturnNull_WhenNotFound()
    {
        // Act
        var result = await _store.GetDocumentAsync("users", "non-existing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AppendOplogEntryAsync_ShouldPersistEntry()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new { Name = "Bob" });
        var jsonElement = JsonDocument.Parse(json).RootElement;
        var entry = new OplogEntry("users", "user1", OperationType.Put, jsonElement, new HlcTimestamp(2000, 0, "test-node"));

        // Act
        await _store.AppendOplogEntryAsync(entry);

        // Assert
        var oplog = await _store.GetOplogAfterAsync(new HlcTimestamp(1000, 0, "test-node"));
        oplog.Should().ContainSingle();
        oplog.First().Key.Should().Be("user1");
    }

    [Fact]
    public async Task GetLatestTimestampAsync_ShouldReturnLatest()
    {
        // Arrange
        var entry1 = new OplogEntry("users", "user1", OperationType.Put, null, new HlcTimestamp(1000, 0, "node1"));
        var entry2 = new OplogEntry("users", "user2", OperationType.Put, null, new HlcTimestamp(3000, 5, "node2"));

        await _store.AppendOplogEntryAsync(entry1);
        await _store.AppendOplogEntryAsync(entry2);

        // Act
        var latest = await _store.GetLatestTimestampAsync();

        // Assert
        latest.PhysicalTime.Should().Be(3000);
        latest.LogicalCounter.Should().Be(5);
    }

    [Fact]
    public async Task ApplyBatchAsync_ShouldMergeDocuments_LastWriteWins()
    {
        // Arrange - Save initial document
        var initialDoc = CreateDocument("users", "user1", new { Name = "Alice", Age = 30 }, new HlcTimestamp(1000, 0, "node1"));
        await _store.SaveDocumentAsync(initialDoc);

        // Incoming document with newer timestamp
        var newerDoc = CreateDocument("users", "user1", new { Name = "Alice Updated", Age = 31 }, new HlcTimestamp(2000, 0, "node2"));
        var json = JsonSerializer.Serialize(new { Name = "Alice Updated", Age = 31 });
        var jsonElement = JsonDocument.Parse(json).RootElement;
        var oplogEntry = new OplogEntry("users", "user1", OperationType.Put, jsonElement, new HlcTimestamp(2000, 0, "node2"));

        // Act
        await _store.ApplyBatchAsync(new[] { newerDoc }, new[] { oplogEntry });

        // Assert
        var result = await _store.GetDocumentAsync("users", "user1");
        result.Should().NotBeNull();
        result!.Content.GetProperty("Name").GetString().Should().Be("Alice Updated");
        result.UpdatedAt.PhysicalTime.Should().Be(2000);
    }

    [Fact]
    public async Task QueryDocumentsAsync_ShouldSupportPaging()
    {
        // Arrange - Create 10 documents
        for (int i = 0; i < 10; i++)
        {
            var doc = CreateDocument("users", $"user{i}", new { Name = $"User{i}", Age = 20 + i }, new HlcTimestamp(1000 + i, 0, "node1"));
            await _store.SaveDocumentAsync(doc);
        }

        // Act - Skip 3, Take 5
        var results = await _store.QueryDocumentsAsync("users", null, skip: 3, take: 5);

        // Assert
        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetCollectionsAsync_ShouldReturnDistinctCollectionNames()
    {
        // Arrange
        await _store.SaveDocumentAsync(CreateDocument("users", "u1", new { }, new HlcTimestamp(100, 0, "n1")));
        await _store.SaveDocumentAsync(CreateDocument("products", "p1", new { }, new HlcTimestamp(100, 0, "n1")));
        await _store.SaveDocumentAsync(CreateDocument("users", "u2", new { }, new HlcTimestamp(100, 0, "n1"))); // Duplicate collection

        // Act
        var collections = await _store.GetCollectionsAsync();

        // Assert
        collections.Should().HaveCount(2);
        collections.Should().Contain(new[] { "users", "products" });
    }

    [Fact]
    public async Task EnsureIndexAsync_ShouldCreateIndexWithoutError()
    {
        // Arrange
        await _store.SaveDocumentAsync(CreateDocument("users", "u1", new { Age = 30 }, new HlcTimestamp(100, 0, "n1")));

        // Act & Assert
        // SQLite silently ignores if index exists (IF NOT EXISTS), so running twice should be fine
        await _store.EnsureIndexAsync("users", "Age");
        await _store.EnsureIndexAsync("users", "Age");
        
        // No exception means pass
    }

    [Fact]
    public async Task ApplyBatchAsync_WithEmptyDocsAndOplogWithPayload_ShouldApplyChanges()
    {
        // Arrange - Save initial document
        var initialDoc = CreateDocument("users", "user1", new { Name = "Alice", Age = 30 }, new HlcTimestamp(1000, 0, "node1"));
        await _store.SaveDocumentAsync(initialDoc);

        // Create oplog entry with payload for update (newer timestamp)
        var json = JsonSerializer.Serialize(new { Name = "Alice Updated", Age = 31 });
        var jsonElement = JsonDocument.Parse(json).RootElement;
        var oplogEntry = new OplogEntry("users", "user1", OperationType.Put, jsonElement, new HlcTimestamp(2000, 0, "node2"));

        // Act - Apply batch with empty documents list but oplog entries
        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), new[] { oplogEntry });

        // Assert - Document should be updated from oplog entry
        var result = await _store.GetDocumentAsync("users", "user1");
        result.Should().NotBeNull();
        result!.Content.GetProperty("Name").GetString().Should().Be("Alice Updated");
        result.Content.GetProperty("Age").GetInt32().Should().Be(31);
        result.UpdatedAt.PhysicalTime.Should().Be(2000);
    }

    [Fact]
    public async Task ApplyBatchAsync_WithPutWithoutPayload_ShouldRejectAndNotWriteOplog()
    {
        // Arrange - Save initial document
        var initialDoc = CreateDocument("users", "user1", new { Name = "Alice", Age = 30 }, new HlcTimestamp(1000, 0, "node1"));
        await _store.SaveDocumentAsync(initialDoc);

        // Create oplog entry WITHOUT payload (should be rejected)
        var oplogEntry = new OplogEntry("users", "user1", OperationType.Put, null, new HlcTimestamp(2000, 0, "node2"));

        // Act - Apply batch with Put but no payload
        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), new[] { oplogEntry });

        // Assert - Document should remain unchanged
        var result = await _store.GetDocumentAsync("users", "user1");
        result.Should().NotBeNull();
        result!.Content.GetProperty("Name").GetString().Should().Be("Alice");
        result.Content.GetProperty("Age").GetInt32().Should().Be(30);
        result.UpdatedAt.PhysicalTime.Should().Be(1000);

        // Oplog should not contain the rejected entry
        var oplog = await _store.GetOplogAfterAsync(new HlcTimestamp(1500, 0, "node1"));
        oplog.Should().BeEmpty(); // No entries after timestamp 1500
    }
}
