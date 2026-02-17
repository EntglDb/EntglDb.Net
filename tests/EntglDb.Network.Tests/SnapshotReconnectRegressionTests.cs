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
                IOplogStore oplogStore,
                IDocumentStore documentStore,
                ISnapshotMetadataStore snapshotMetadataStore,
                ISnapshotService snapshotService,
                IPeerNodeConfigurationProvider peerNodeConfigurationProvider)
                : base(discovery, oplogStore, documentStore, snapshotMetadataStore, snapshotService, peerNodeConfigurationProvider, NullLoggerFactory.Instance)
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

        private class MockSnapshotMetadataStore : ISnapshotMetadataStore
        {
            public Task DropAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IEnumerable<SnapshotMetadata>> ExportAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<SnapshotMetadata?> GetSnapshotMetadataAsync(string nodeId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<SnapshotMetadata?>(null);
            }

            public Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<string?>(null);
            }

            public Task<IEnumerable<SnapshotMetadata>> GetAllSnapshotMetadataAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IEnumerable<SnapshotMetadata>>(Array.Empty<SnapshotMetadata>());
            }

            public Task ImportAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task MergeAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task UpdateSnapshotMetadataAsync(SnapshotMetadata existingMeta, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private class MockSnapshotService : ISnapshotService
        {
            public Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ClearAllDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class StubDocumentStore : IDocumentStore
        {
            public event Action<OplogEntry>? LocalOplogEntryCreated;
            public IEnumerable<string> InterestedCollection => new[] { "Users", "TodoLists" };
            public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
            public Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
            public Task<IEnumerable<Document>> GetDocumentsAsync(List<(string Collection, string Key)> documentKeys, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
            public Task<bool> PutDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<Document> MergeAsync(Document incoming, CancellationToken cancellationToken = default) => Task.FromResult(incoming);
            public Task DropAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<Document>> ExportAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
            public Task ImportAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task MergeAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class MockOplogStore : IOplogStore
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
            public Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task BackupAsync(string backupPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(new List<string>());
            public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
            public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<OplogEntry?>(null);
            public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, "test"));
            public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
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

            public Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task DropAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
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
            var oplogStore = new MockOplogStore();
            oplogStore.SnapshotHashToReturn = "snapshot-boundary-hash";
            oplogStore.LocalHeadHashToReturn = "snapshot-boundary-hash"; 
            var snapshotMetadataStore = new MockSnapshotMetadataStore();
            var snapshotService = new MockSnapshotService();

            var orch = new TestableSyncOrchestrator(new StubDiscovery(), oplogStore, new StubDocumentStore(), snapshotMetadataStore, snapshotService, new StubConfig());

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
            var oplogStore = new MockOplogStore();
            oplogStore.SnapshotHashToReturn = "snapshot-boundary-hash";
            oplogStore.LocalHeadHashToReturn = "some-old-hash"; 
            var snapshotMetadataStore = new MockSnapshotMetadataStore();
            var snapshotService = new MockSnapshotService();

            var orch = new TestableSyncOrchestrator(new StubDiscovery(), oplogStore, new StubDocumentStore(), snapshotMetadataStore, snapshotService, new StubConfig());
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
