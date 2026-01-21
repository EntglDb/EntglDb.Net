using EntglDb.Core;
using EntglDb.Core.Sync;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class GapDetectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqlitePeerStore _store;
    private readonly GapDetectionService _gapDetection;

    public GapDetectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-gap-{Guid.NewGuid()}.db");
        _store = new SqlitePeerStore($"Data Source={_dbPath}");
        _gapDetection = new GapDetectionService(_store);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private static OplogEntry CreateOplogEntry(string collection, string key, long physicalTime, int logicalCounter, string nodeId, object? data = null)
    {
        JsonElement? payload = null;
        if (data != null)
        {
            var json = JsonSerializer.Serialize(data);
            payload = JsonDocument.Parse(json).RootElement;
        }
        return new OplogEntry(collection, key, OperationType.Put, payload, new HlcTimestamp(physicalTime, logicalCounter, nodeId));
    }

    [Fact]
    public async Task GapDetection_ShouldSeedFromPersistentState()
    {
        // Arrange - Add some entries to the oplog
        var entry1 = CreateOplogEntry("users", "user1", 1000, 0, "node1", new { Name = "Alice" });
        var entry2 = CreateOplogEntry("users", "user2", 2000, 0, "node1", new { Name = "Bob" });
        var entry3 = CreateOplogEntry("users", "user3", 1500, 0, "node2", new { Name = "Charlie" });

        await _store.AppendOplogEntryAsync(entry1);
        await _store.AppendOplogEntryAsync(entry2);
        await _store.AppendOplogEntryAsync(entry3);

        // Act - Seed the gap detection
        await _gapDetection.EnsureSeededAsync();

        // Assert - Should track highest timestamp per node
        var node1Highest = _gapDetection.GetHighestContiguousTimestamp("node1");
        var node2Highest = _gapDetection.GetHighestContiguousTimestamp("node2");

        node1Highest.PhysicalTime.Should().Be(2000);
        node2Highest.PhysicalTime.Should().Be(1500);
    }

    [Fact]
    public async Task GapDetection_ShouldNotReRequestGapsAfterRestart()
    {
        // Arrange - Simulate first run: add entries and seed
        var entry1 = CreateOplogEntry("users", "user1", 1000, 0, "node1", new { Name = "Alice" });
        var entry2 = CreateOplogEntry("users", "user2", 2000, 0, "node1", new { Name = "Bob" });

        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), new[] { entry1, entry2 });

        // First gap detection instance - seeds and updates
        var gapDetection1 = new GapDetectionService(_store);
        await gapDetection1.EnsureSeededAsync();
        gapDetection1.UpdateAfterApplyBatch(new[] { entry1, entry2 });

        var sequences1 = gapDetection1.GetAllSequences();
        sequences1["node1"].Should().Be(2000);

        // Act - Simulate restart: create new gap detection service and seed from persistent state
        var gapDetection2 = new GapDetectionService(_store);
        await gapDetection2.EnsureSeededAsync();

        // Assert - Should not detect gaps for already synchronized entries
        var hasGap = gapDetection2.HasGap("node1", new HlcTimestamp(2000, 0, "node1"));
        hasGap.Should().BeFalse("entries up to 2000 were already synchronized");

        // Should detect gap for newer entries
        var hasGapNewer = gapDetection2.HasGap("node1", new HlcTimestamp(3000, 0, "node1"));
        hasGapNewer.Should().BeTrue("entries after 2000 are not yet synchronized");
    }

    [Fact]
    public async Task GapDetection_ShouldUpdateAfterBackfill()
    {
        // Arrange - Start with some entries
        var entry1 = CreateOplogEntry("users", "user1", 1000, 0, "node1", new { Name = "Alice" });
        await _store.AppendOplogEntryAsync(entry1);

        await _gapDetection.EnsureSeededAsync();

        // Simulate detecting a gap
        var hasGap = _gapDetection.HasGap("node1", new HlcTimestamp(3000, 0, "node1"));
        hasGap.Should().BeTrue();

        // Act - Backfill missing entries
        var entry2 = CreateOplogEntry("users", "user2", 2000, 0, "node1", new { Name = "Bob" });
        var entry3 = CreateOplogEntry("users", "user3", 3000, 0, "node1", new { Name = "Charlie" });

        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), new[] { entry2, entry3 });
        _gapDetection.UpdateAfterApplyBatch(new[] { entry2, entry3 });

        // Assert - Gap should be filled
        var hasGapAfter = _gapDetection.HasGap("node1", new HlcTimestamp(3000, 0, "node1"));
        hasGapAfter.Should().BeFalse("gap was filled by backfill");

        var sequences = _gapDetection.GetAllSequences();
        sequences["node1"].Should().Be(3000);
    }

    [Fact]
    public async Task GapDetection_ShouldTrackMultipleNodes()
    {
        // Arrange
        var entry1 = CreateOplogEntry("users", "user1", 1000, 0, "node1", new { Name = "Alice" });
        var entry2 = CreateOplogEntry("users", "user2", 2000, 0, "node2", new { Name = "Bob" });
        var entry3 = CreateOplogEntry("users", "user3", 3000, 0, "node3", new { Name = "Charlie" });

        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), new[] { entry1, entry2, entry3 });

        // Act
        await _gapDetection.EnsureSeededAsync();
        _gapDetection.UpdateAfterApplyBatch(new[] { entry1, entry2, entry3 });

        // Assert
        var sequences = _gapDetection.GetAllSequences();
        sequences.Should().HaveCount(3);
        sequences["node1"].Should().Be(1000);
        sequences["node2"].Should().Be(2000);
        sequences["node3"].Should().Be(3000);
    }
}
