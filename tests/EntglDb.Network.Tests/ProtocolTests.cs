using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EntglDb.Network.Proto;
using EntglDb.Network.Protocol;
using EntglDb.Network.Security;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class ProtocolTests
    {
        private readonly ProtocolHandler _handler;

        public ProtocolTests()
        {
            _handler = new ProtocolHandler(NullLogger.Instance);
        }

        [Fact]
        public async Task RoundTrip_ShouldWorks_WithPlainMessage()
        {
            // Arrange
            var stream = new MemoryStream();
            var message = new HandshakeRequest { NodeId = "node-1", AuthToken = "token" };

            // Act
            await _handler.SendMessageAsync(stream, MessageType.HandshakeReq, message, false, null);
            
            stream.Position = 0; // Reset for reading
            var (type, payload) = await _handler.ReadMessageAsync(stream, null);

            // Assert
            type.Should().Be(MessageType.HandshakeReq);
            var decoded = HandshakeRequest.Parser.ParseFrom(payload);
            decoded.NodeId.Should().Be("node-1");
            decoded.AuthToken.Should().Be("token");
        }

        [Fact]
        public async Task RoundTrip_ShouldWork_WithCompression()
        {
            // Arrange
            var stream = new MemoryStream();
            // Create a large message to trigger compression logic (threshold is small but let's be safe)
            var largeData = string.Join("", Enumerable.Repeat("ABCDEF0123456789", 100));
            var message = new HandshakeRequest { NodeId = largeData, AuthToken = "token" };

            // Act
            await _handler.SendMessageAsync(stream, MessageType.HandshakeReq, message, true, null);
            
            stream.Position = 0;
            var (type, payload) = await _handler.ReadMessageAsync(stream, null);

            // Assert
            type.Should().Be(MessageType.HandshakeReq);
            var decoded = HandshakeRequest.Parser.ParseFrom(payload);
            decoded.NodeId.Should().Be(largeData);
        }

        [Fact]
        public async Task RoundTrip_ShouldWork_WithEncryption()
        {
            // Arrange
            var stream = new MemoryStream();
            var message = new HandshakeRequest { NodeId = "secure-node", AuthToken = "secure-token" };
            
            // Mock CipherState
            var key = new byte[32]; // 256-bit key
            new Random().NextBytes(key);
            var cipherState = new CipherState(key, key); // Encrypt and Decrypt with same key for loopback

            // Act
            await _handler.SendMessageAsync(stream, MessageType.HandshakeReq, message, false, cipherState);
            
            stream.Position = 0;
            var (type, payload) = await _handler.ReadMessageAsync(stream, cipherState);

            // Assert
            type.Should().Be(MessageType.HandshakeReq);
            var decoded = HandshakeRequest.Parser.ParseFrom(payload);
            decoded.NodeId.Should().Be("secure-node");
        }

        [Fact]
        public async Task RoundTrip_ShouldWork_WithEncryption_And_Compression()
        {
            // Arrange
            var stream = new MemoryStream();
            var largeData = string.Join("", Enumerable.Repeat("SECURECOMPRESSION", 100));
            var message = new HandshakeRequest { NodeId = largeData };
            
            var key = new byte[32]; 
            new Random().NextBytes(key);
            var cipherState = new CipherState(key, key);

            // Act: Compress THEN Encrypt
            await _handler.SendMessageAsync(stream, MessageType.HandshakeReq, message, true, cipherState);
            
            stream.Position = 0;
            // Verify wire encryption (should be MessageType.SecureEnv)
            // But ReadMessageAsync abstracts this away. 
            // We can peek at the stream if we want, but let's trust ReadMessageAsync handles it.
            
            var (type, payload) = await _handler.ReadMessageAsync(stream, cipherState);

            // Assert
            type.Should().Be(MessageType.HandshakeReq);
            var decoded = HandshakeRequest.Parser.ParseFrom(payload);
            decoded.NodeId.Should().Be(largeData);
        }

        [Fact]
        public async Task ReadMessage_ShouldHandle_Fragmentation()
        {
            // Arrange
            var fullStream = new MemoryStream();
            var message = new HandshakeRequest { NodeId = "fragmented" };
            await _handler.SendMessageAsync(fullStream, MessageType.HandshakeReq, message, false, null);
            
            byte[] completeBytes = fullStream.ToArray();
            var fragmentedStream = new FragmentedMemoryStream(completeBytes, chunkSize: 2); // Read 2 bytes at a time

            // Act
            var (type, payload) = await _handler.ReadMessageAsync(fragmentedStream, null);

            // Assert
            type.Should().Be(MessageType.HandshakeReq);
            var decoded = HandshakeRequest.Parser.ParseFrom(payload);
            decoded.NodeId.Should().Be("fragmented");
        }

        // Helper Stream for fragmentation test
        private class FragmentedMemoryStream : MemoryStream
        {
            private readonly int _chunkSize;

            public FragmentedMemoryStream(byte[] buffer, int chunkSize) : base(buffer)
            {
                _chunkSize = chunkSize;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                // Force read to be max _chunkSize, even if more is requested
                int toRead = Math.Min(count, _chunkSize);
                return await base.ReadAsync(buffer, offset, toRead, cancellationToken);
            }
        }
    }
}
