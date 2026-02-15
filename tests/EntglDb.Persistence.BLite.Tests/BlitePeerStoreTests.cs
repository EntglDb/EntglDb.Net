using System.Text.Json;
using EntglDb.Core;
using EntglDb.Persistence.Blite;
using FluentAssertions;

namespace EntglDb.Persistence.Blite.Tests;

public class BlitePeerStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BlitePeerStore _store;

    public BlitePeerStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"entgldb_test_{Guid.NewGuid():N}.db");
        _store = new BlitePeerStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        var walPath = Path.ChangeExtension(_dbPath, ".wal");
        if (File.Exists(walPath)) File.Delete(walPath);
    }

    [Fact]
    public async Task Save_And_Get_Document_Should_Work()
    {
        // Arrange
        var content = JsonDocument.Parse("{\"name\":\"Test\",\"value\":123}").RootElement;
        var timestamp = new HlcTimestamp(123456789, 1, "node1");
        var doc = new Document("users", "user1", content, timestamp, false);

        // Act
        await _store.SaveDocumentAsync(doc);
        var retrieved = await _store.GetDocumentAsync("users", "user1");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Key.Should().Be("user1");
        retrieved.Collection.Should().Be("users");
        retrieved.UpdatedAt.Should().Be(timestamp);
        retrieved.Content.GetProperty("name").GetString().Should().Be("Test");
        retrieved.Content.GetProperty("value").GetInt32().Should().Be(123);
    }

    [Fact]
    public async Task Append_And_Get_Oplog_Should_Work()
    {
        // Arrange
        var timestamp = new HlcTimestamp(123456789, 1, "node1");
        var entry = new OplogEntry("users", "user1", OperationType.Put, null, timestamp, "prev_hash");

        // Act
        await _store.AppendOplogEntryAsync(entry);
        var retrieved = await _store.GetOplogAfterAsync(new HlcTimestamp(0, 0, ""), CancellationToken.None);

        // Assert
        retrieved.Should().HaveCount(1);
        var first = retrieved.First();
        first.Hash.Should().Be(entry.Hash);
        first.Collection.Should().Be("users");
        first.Key.Should().Be("user1");
        first.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public async Task ApplyBatch_Should_Be_Atomic()
    {
        // Arrange
        var timestamp = new HlcTimestamp(123456789, 1, "node1");
        var doc1 = new Document("users", "u1", JsonDocument.Parse("{}").RootElement, timestamp, false);
        var doc2 = new Document("users", "u2", JsonDocument.Parse("{}").RootElement, timestamp, false);
        var entry1 = new OplogEntry("users", "u1", OperationType.Put, null, timestamp, "");
        var entry2 = new OplogEntry("users", "u2", OperationType.Put, null, timestamp, entry1.Hash);

        // Act
        await _store.ApplyBatchAsync(new[] { doc1, doc2 }, new[] { entry1, entry2 });

        // Assert
        var d1 = await _store.GetDocumentAsync("users", "u1");
        var d2 = await _store.GetDocumentAsync("users", "u2");
        var oplog = await _store.GetOplogAfterAsync(new HlcTimestamp(0, 0, ""));

        d1.Should().NotBeNull();
        d2.Should().NotBeNull();
        oplog.Should().HaveCount(2);
    }

    [Fact]
    public async Task Query_Documents_Should_Filter_Correctly()
    {
        // Arrange
        var t = new HlcTimestamp(1, 1, "n1");
        await _store.SaveDocumentAsync(new Document("tasks", "t1", JsonDocument.Parse("{\"status\":\"open\",\"priority\":1}").RootElement, t, false));
        await _store.SaveDocumentAsync(new Document("tasks", "t2", JsonDocument.Parse("{\"status\":\"closed\",\"priority\":2}").RootElement, t, false));
        await _store.SaveDocumentAsync(new Document("tasks", "t3", JsonDocument.Parse("{\"status\":\"open\",\"priority\":3}").RootElement, t, false));

        // Act
        var query = new Eq("status", "open");
        var results = await _store.QueryDocumentsAsync("tasks", query);

        // Assert
        results.Should().HaveCount(2);
        results.All(d => d.Content.GetProperty("status").GetString() == "open").Should().BeTrue();
    }
}
