using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Network.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class SecureHandshakeTests
    {
        [Fact]
        public async Task Handshake_Should_Succeed_Between_Two_Services()
        {
            // Arrange
            var clientStream = new PipeStream();
            var serverStream = new PipeStream();
            
            // Client writes to clientStream, server reads from clientStream
            // Server writes to serverStream, client reads from serverStream
            
            var clientSocket = new DuplexStream(serverStream, clientStream); // Read from server, Write to client
            var serverSocket = new DuplexStream(clientStream, serverStream); // Read from client, Write to server

            var clientService = new SecureHandshakeService(NullLogger<SecureHandshakeService>.Instance);
            var serverService = new SecureHandshakeService(NullLogger<SecureHandshakeService>.Instance);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var clientTask = clientService.HandshakeAsync(clientSocket, isInitiator: true, myNodeId: "client", token: cts.Token);
            var serverTask = serverService.HandshakeAsync(serverSocket, isInitiator: false, myNodeId: "server", token: cts.Token);

            await Task.WhenAll(clientTask, serverTask);

            // Assert
            var clientState = clientTask.Result;
            var serverState = serverTask.Result;

            clientState.Should().NotBeNull();
            serverState.Should().NotBeNull();
            
            // Keys should match (Symmetric)
            clientState!.EncryptKey.Should().BeEquivalentTo(serverState!.DecryptKey);
            clientState.DecryptKey.Should().BeEquivalentTo(serverState.EncryptKey);
        }

        // Simulates a pipe. Writes go to buffer, Reads drain buffer.
        class SimplexStream : MemoryStream
        {
            // Simple approach: Use one MemoryStream as a shared buffer?
            // No, MemoryStream is not thread safe for concurrent Read/Write in this pipe manner really.
            // Better to use a producer/consumer stream but for simplicity let's use a basic blocking queue logic or just wait.
            // Actually, for unit tests, strictly ordered operations are better. But handshake is interactive.
            // We need a proper pipe.
        }

        // Let's use a simple PipeStream implementation using SemaphoreSlim for sync
        class PipeStream : Stream
        {
            private readonly MemoryStream _buffer = new MemoryStream();
            private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(0);
            private readonly object _lock = new object();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _buffer.Length;
            public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException("Use Async");

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _readSemaphore.WaitAsync(cancellationToken);
                lock (_lock)
                {
                    _buffer.Position = 0;
                    int read = _buffer.Read(buffer, offset, count);
                    
                    // Compact buffer (inefficient but works for unit tests)
                    byte[] remaining = _buffer.ToArray().Skip(read).ToArray();
                    _buffer.SetLength(0);
                    _buffer.Write(remaining, 0, remaining.Length);
                    
                    if (_buffer.Length > 0) _readSemaphore.Release(); // Signal if data remains
                    
                    return read;
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_lock)
                {
                    long pos = _buffer.Position;
                    _buffer.Seek(0, SeekOrigin.End);
                    _buffer.Write(buffer, offset, count);
                    _buffer.Position = pos;
                }
                _readSemaphore.Release();
            }
        }

        class DuplexStream : Stream
        {
            private readonly Stream _readSource;
            private readonly Stream _writeTarget;

            public DuplexStream(Stream readSource, Stream writeTarget)
            {
                _readSource = readSource;
                _writeTarget = writeTarget;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }

            public override void Flush() => _writeTarget.Flush();

            public override int Read(byte[] buffer, int offset, int count) => _readSource.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) 
                => _readSource.ReadAsync(buffer, offset, count, cancellationToken);

            public override void Write(byte[] buffer, int offset, int count) => _writeTarget.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => _writeTarget.WriteAsync(buffer, offset, count, cancellationToken);
            
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
