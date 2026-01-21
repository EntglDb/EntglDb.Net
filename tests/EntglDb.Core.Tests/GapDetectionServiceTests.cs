using EntglDb.Core;
using EntglDb.Core.Sync;
using EntglDb.Core.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EntglDb.Core.Tests;

public class GapDetectionServiceTests
{
    private class InMemoryStore : IPeerStore
    {
        private readonly Dictionary<string, long> _sequences = new();
        private readonly List<OplogEntry> _entries = new();

        public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

        public Task<Dictionary<string, long>> GetPeerSequenceNumbersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Dictionary<string, long>(_sequences));
        }

        public void AddSequence(string nodeId, long sequenceNumber)
        {
            _sequences[nodeId] = sequenceNumber;
        }

        public void AddEntry(OplogEntry entry)
        {
            _entries.Add(entry);
        }

        public Task<IEnumerable<OplogEntry>> GetOplogBySequenceNumbersAsync(string nodeId, IEnumerable<long> sequenceNumbers, CancellationToken cancellationToken = default)
        {
            var result = _entries.Where(e => e.Timestamp.NodeId == nodeId && sequenceNumbers.Contains(e.SequenceNumber));
            return Task.FromResult(result);
        }

        // Stub implementations for other IPeerStore methods
        public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
        public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<OplogEntry>());
        public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, ""));
        public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<Document>());
        public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<string>());
        public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveRemotePeerAsync(Network.RemotePeerConfiguration peer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<Network.RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<Network.RemotePeerConfiguration>());
        public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> GetCurrentSequenceNumberAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }

    [Fact]
    public async Task DetectGapsAsync_ShouldIdentifyMissingSequences()
    {
        // Arrange
        var store = new InMemoryStore();
        var service = new GapDetectionService(store, NullLogger<GapDetectionService>.Instance);

        // Simulate: Local node has received sequences 1, 2, 4, 5 from "node-A"
        // (sequence 3 is missing)
        service.RecordReceivedEntries(new[]
        {
            new OplogEntry("col", "k1", OperationType.Put, null, new HlcTimestamp(100, 0, "node-A"), 1),
            new OplogEntry("col", "k2", OperationType.Put, null, new HlcTimestamp(200, 0, "node-A"), 2),
            new OplogEntry("col", "k4", OperationType.Put, null, new HlcTimestamp(400, 0, "node-A"), 4),
            new OplogEntry("col", "k5", OperationType.Put, null, new HlcTimestamp(500, 0, "node-A"), 5),
        });

        // Remote node reports it has up to sequence 5
        var peerSequences = new Dictionary<string, long>
        {
            { "node-A", 5 }
        };

        // Act
        var gaps = await service.DetectGapsAsync("node-A", peerSequences);

        // Assert
        gaps.Should().ContainSingle();
        gaps.Should().Contain(3);
    }

    [Fact]
    public async Task DetectGapsAsync_ShouldReturnEmpty_WhenNoGaps()
    {
        // Arrange
        var store = new InMemoryStore();
        var service = new GapDetectionService(store, NullLogger<GapDetectionService>.Instance);

        // All sequences received (1, 2, 3)
        service.RecordReceivedEntries(new[]
        {
            new OplogEntry("col", "k1", OperationType.Put, null, new HlcTimestamp(100, 0, "node-B"), 1),
            new OplogEntry("col", "k2", OperationType.Put, null, new HlcTimestamp(200, 0, "node-B"), 2),
            new OplogEntry("col", "k3", OperationType.Put, null, new HlcTimestamp(300, 0, "node-B"), 3),
        });

        var peerSequences = new Dictionary<string, long>
        {
            { "node-B", 3 }
        };

        // Act
        var gaps = await service.DetectGapsAsync("node-B", peerSequences);

        // Assert
        gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectGapsAsync_ShouldIdentifyMultipleGaps()
    {
        // Arrange
        var store = new InMemoryStore();
        var service = new GapDetectionService(store, NullLogger<GapDetectionService>.Instance);

        // Received: 1, 2, 5, 7 (missing 3, 4, 6)
        service.RecordReceivedEntries(new[]
        {
            new OplogEntry("col", "k1", OperationType.Put, null, new HlcTimestamp(100, 0, "node-C"), 1),
            new OplogEntry("col", "k2", OperationType.Put, null, new HlcTimestamp(200, 0, "node-C"), 2),
            new OplogEntry("col", "k5", OperationType.Put, null, new HlcTimestamp(500, 0, "node-C"), 5),
            new OplogEntry("col", "k7", OperationType.Put, null, new HlcTimestamp(700, 0, "node-C"), 7),
        });

        var peerSequences = new Dictionary<string, long>
        {
            { "node-C", 7 }
        };

        // Act
        var gaps = await service.DetectGapsAsync("node-C", peerSequences);

        // Assert
        gaps.Should().HaveCount(3);
        gaps.Should().Contain(new[] { 3L, 4L, 6L });
    }

    [Fact]
    public void GetStatus_ShouldReturnTrackingInformation()
    {
        // Arrange
        var store = new InMemoryStore();
        var service = new GapDetectionService(store, NullLogger<GapDetectionService>.Instance);

        service.RecordReceivedEntries(new[]
        {
            new OplogEntry("col", "k1", OperationType.Put, null, new HlcTimestamp(100, 0, "node-D"), 1),
            new OplogEntry("col", "k2", OperationType.Put, null, new HlcTimestamp(200, 0, "node-D"), 2),
            new OplogEntry("col", "k3", OperationType.Put, null, new HlcTimestamp(300, 0, "node-D"), 3),
        });

        // Act
        var status = service.GetStatus();

        // Assert
        status.HighestContiguousPerNode.Should().ContainKey("node-D");
        status.HighestContiguousPerNode["node-D"].Should().Be(3);
    }
}
