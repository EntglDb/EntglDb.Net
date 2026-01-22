using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Network.Security;
using EntglDb.Core.Storage;
using EntglDb.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class ConnectionTests
    {
        [Fact]
        public async Task Server_Should_Reject_Clients_When_Limit_Reached()
        {
            // Arrange
            var configProvider = new StubConfigProvider();
            var server = new TcpSyncServer(
                new StubStore(),
                configProvider,
                NullLogger<TcpSyncServer>.Instance,
                new StubAuthenticator(),
                new SpyHandshakeService()
            );

            // Set low limit for testing
            server.MaxConnections = 2;

            await server.Start();
            int port = server.ListeningPort ?? throw new Exception("Server not started");

            using var client1 = new TcpClient();
            using var client2 = new TcpClient();
            using var client3 = new TcpClient();

            try
            {
                // Act
                await client1.ConnectAsync("127.0.0.1", port);
                await client2.ConnectAsync("127.0.0.1", port);

                // Give server a moment to accept and increment counters (it happens async on thread pool)
                await Task.Delay(100);

                await client3.ConnectAsync("127.0.0.1", port);

                // Assert
                // Client 3 should be disconnected immediately.
                // Depending on timing, ConnectAsync might succeed, but subsequent Read should return 0 (EOF).
                var stream3 = client3.GetStream();
                byte[] buffer = new byte[10];
                int read = await stream3.ReadAsync(buffer, 0, 10);
                
                read.Should().Be(0, "Server should close connection immediately for client 3");
                
                // Verify Clients 1 and 2 are still connected (simple check)
                client1.Connected.Should().BeTrue();
                client2.Connected.Should().BeTrue();
            }
            finally
            {
                await server.Stop();
            }
        }

        // Stubs (Nested to avoid namespace conflict)
        private class StubConfigProvider : IPeerNodeConfigurationProvider
        {
            public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
            public Task<PeerNodeConfiguration> GetConfiguration()
            {
                return Task.FromResult(new PeerNodeConfiguration
                {
                    NodeId = "server-node",
                    AuthToken = "auth-token",
                    TcpPort = 0 
                });
            }
        }

        private class StubStore : IPeerStore
        {
            public event EventHandler<ChangesAppliedEventArgs> ChangesApplied;
            public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HlcTimestamp(0, 0, "node"));
            public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default) => Task.FromResult(new VectorClock());
            public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            public Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
            
            // Dummy impls
            public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
            public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Document>>(new List<Document>());
            public Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(new List<string>());
            public Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<RemotePeerConfiguration>>(new List<RemotePeerConfiguration>());
            public Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<bool> CheckIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task BackupAsync(string backupPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
            public Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<OplogEntry?>(null);
            public Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OplogEntry>>(new List<OplogEntry>());
        }

        private class StubAuthenticator : IAuthenticator
        {
            public Task<bool> ValidateAsync(string nodeId, string authToken) => Task.FromResult(true);
        }

        private class SpyHandshakeService : IPeerHandshakeService
        {
            public Task<CipherState?> HandshakeAsync(System.IO.Stream stream, bool isInitiator, string localNodeId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<CipherState?>(null);
            }
        }
    }
}
