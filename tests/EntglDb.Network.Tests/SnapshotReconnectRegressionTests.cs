using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class SnapshotReconnectRegressionTests
    {
        // Subclass to expose private method
        private class TestableSyncOrchestrator : SyncOrchestrator
        {
            public TestableSyncOrchestrator(
                IDiscoveryService discovery,
                IPeerStore store,
                IPeerNodeConfigurationProvider peerNodeConfigurationProvider)
                : base(discovery, store, peerNodeConfigurationProvider, NullLoggerFactory.Instance)
            {
            }

            public async Task<string> TestProcessInboundBatchAsync(
                TcpPeerClient client, 
                string peerNodeId, 
                IList<OplogEntry> changes, 
                CancellationToken token)
            {
                // Reflection to invoke private method since it's private not protected
                var method = typeof(SyncOrchestrator).GetMethod(
                    "ProcessInboundBatchAsync", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (method == null)
                    throw new InvalidOperationException("ProcessInboundBatchAsync method not found.");

                try
                {
                    var task = (Task)method.Invoke(this, new object[] { client, peerNodeId, changes, token })!;
                    await task.ConfigureAwait(false);
                    
                    // Access .Result via reflection because generic type is private
                    var resultProp = task.GetType().GetProperty("Result");
                    var result = resultProp?.GetValue(task);
                    
                    return result?.ToString() ?? "null";
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    if (ex.InnerException != null) throw ex.InnerException;
                    throw;
                }
            }
        }

        private class MockSnapshotStore : IPeerStore
        {
            public string? SnapshotHashToReturn { get; set; }
            public string? LocalHeadHashToReturn { get; set; }

            public Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(SnapshotHashToReturn);
            }

            public Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(LocalHeadHashToReturn);
            }
            
            // Stubs for other methods
            public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;
            public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task BackupAsync(string backupPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(new List<string>());
            public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
            public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<OplogEntry?>(null);
            public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, "test"));
            public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<RemotePeerConfiguration>>(new List<RemotePeerConfiguration>());
            public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Document>>(Enumerable.Empty<Document>());
            public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default) => Task.FromResult(new VectorClock());
            public Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ClearAllDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        // Mock Client to intercept calls and simulate network behavior
        private class MockTcpPeerClient : TcpPeerClient
        {
            public bool GetChainRangeCalled { get; private set; }

            public MockTcpPeerClient() : base("127.0.0.1:0", NullLogger.Instance)
            {
            }

            public override Task<List<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken token)
            {
                GetChainRangeCalled = true;
                // Simulate the behavior that causes the loop: remote says "I don't have that history, you need a snapshot"
                throw new SnapshotRequiredException();
            }
        }

        private class StubDiscovery : IDiscoveryService
        {
            public IEnumerable<PeerNode> GetActivePeers() => new List<PeerNode>();
            public Task Start() => Task.CompletedTask;
            public Task Stop() => Task.CompletedTask;
        }

        private class StubConfig : IPeerNodeConfigurationProvider
        {
            public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
            public Task<PeerNodeConfiguration> GetConfiguration() => Task.FromResult(new PeerNodeConfiguration { NodeId = "local" });
        }

        [Fact]
        public async Task ProcessInboundBatch_ShouldSkipGapRecovery_WhenEntryMatchesSnapshotBoundary()
        {
            // Arrange
            var store = new MockSnapshotStore();
            store.SnapshotHashToReturn = "snapshot-boundary-hash";
            store.LocalHeadHashToReturn = "snapshot-boundary-hash"; 

            var orch = new TestableSyncOrchestrator(new StubDiscovery(), store, new StubConfig());
            
            // Use Mock Client
            using var client = new MockTcpPeerClient();

            // Incoming entry that connects to snapshot boundary
            var entries = new List<OplogEntry>
            {
                new OplogEntry(
                    "col", "key", OperationType.Put, null, 
                    new HlcTimestamp(100, 1, "remote-node"), 
                    "snapshot-boundary-hash" // PreviousHash matches SnapshotHash!
                ) 
            };

            // Act
            var result = await orch.TestProcessInboundBatchAsync(client, "remote-node", entries, CancellationToken.None);

            // Assert
            result.Should().Be("Success");
            client.GetChainRangeCalled.Should().BeFalse("Should not attempt gap recovery if boundary matches");
        }

        [Fact]
        public async Task ProcessInboundBatch_ShouldTryRecovery_WhenSnapshotMismatch()
        {
             // Arrange
            var store = new MockSnapshotStore();
            store.SnapshotHashToReturn = "snapshot-boundary-hash";
            store.LocalHeadHashToReturn = "some-old-hash"; 

            var orch = new TestableSyncOrchestrator(new StubDiscovery(), store, new StubConfig());
            using var client = new MockTcpPeerClient();

            var entries = new List<OplogEntry>
            {
                new OplogEntry(
                    "col", "key", OperationType.Put, null, 
                    new HlcTimestamp(100, 1, "remote-node"), 
                    "different-hash" // Mismatch!
                )
            };

            // Act & Assert
            // When gap recovery triggers, MockTcpPeerClient throws SnapshotRequiredException.
            // SyncOrchestrator catches SnapshotRequiredException and re-throws it to trigger full sync
            // So we expect SnapshotRequiredException to bubble up (wrapped in TargetInvocationException/AggregateException if not unwrapped by helper)
            
            await Assert.ThrowsAsync<SnapshotRequiredException>(async () => 
                await orch.TestProcessInboundBatchAsync(client, "remote-node", entries, CancellationToken.None));
            
            client.GetChainRangeCalled.Should().BeTrue("Should attempt gap recovery on mismatch");
        }
    }
}
