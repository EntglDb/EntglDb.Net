using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network;
using EntglDb.Network.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class HandshakeRegressionTests
    {
        class StubSnapshotService : ISnapshotService
        {
            public Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        // Stubs
        class StubStore : IOplogStore
        {
            public event EventHandler<ChangesAppliedEventArgs> ChangesApplied;
            public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task BackupAsync(string backupPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public Task EnsureIndexAsync(string collection, string propertyName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(new List<string>());
            public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
            public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, "node"));
            public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default) => Task.FromResult(new VectorClock());
            public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Document>>(new List<Document>());
            public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
            
            public Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<RemotePeerConfiguration>>(new List<RemotePeerConfiguration>());
            public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
            public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<OplogEntry?>(null);
            public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());

            public Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

            public Task ClearAllDataAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
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

        class StubConfigProvider : IPeerNodeConfigurationProvider
        {
            public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
            public Task<PeerNodeConfiguration> GetConfiguration()
            {
                return Task.FromResult(new PeerNodeConfiguration
                {
                    NodeId = "server-node",
                    AuthToken = "auth-token",
                    TcpPort = 0 // Ephemeral port
                });
            }
        }

        class StubAuthenticator : IAuthenticator
        {
            public Task<bool> ValidateAsync(string nodeId, string authToken) => Task.FromResult(true);
        }

        class StubDocumentStore : IDocumentStore
        {
            public event Action<OplogEntry>? LocalOplogEntryCreated;
            public IEnumerable<string> InterestedCollection => new[] { "Users" };
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

        class SpyHandshakeService : IPeerHandshakeService
        {
            public bool HandshakeCalled { get; private set; }
            public Task<CipherState?> HandshakeAsync(Stream stream, bool isInitiator, string localNodeId, CancellationToken cancellationToken = default)
            {
                HandshakeCalled = true;
                return Task.FromResult<CipherState?>(null); // Return null effectively simulating "NoOp" but tracking the call
            }
        }

        [Fact]
        public async Task Server_Should_Call_HandshakeService_On_Client_Connection()
        {
            // Arrange
            var spyHandshake = new SpyHandshakeService();
            var configProvider = new StubConfigProvider();
            
            // We need to use a specific port to connect, 
            // but the StubConfigProvider returns 0 which TcpListener interprets as "pick random".
            // We need to know WHICH port it picked.
            // TcpSyncServer doesn't expose the listener or the port directly after start easily in a race-free way 
            // unless we poll or modify the code.
            // WAIT: TcpSyncServer has a property `ListeningPort`.
            
            var server = new TcpSyncServer(
                new StubStore(),
                new StubDocumentStore(),
                new StubSnapshotService(),
                configProvider,
                NullLogger<TcpSyncServer>.Instance,
                new StubAuthenticator(),
                spyHandshake
            );

            await server.Start();
            int port = server.ListeningPort ?? throw new Exception("Server did not start or report port");

            // Act
            using (var client = new TcpClient())
            {
                await client.ConnectAsync("127.0.0.1", port);
                
                // Allow some time for the server to accept and process the handshake
                await Task.Delay(500);
            }

            await server.Stop();

            // Assert
            spyHandshake.HandshakeCalled.Should().BeTrue("The server must attempt to perform a handshake upon client connection.");
        }
    }
}
