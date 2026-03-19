using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using EntglDb.Network.Telemetry;

namespace EntglDb.Network.Protocol
{
    /// <summary>
    /// Handles the low-level framing, compression, encryption, and serialization of EntglDb messages.
    /// Encapsulates the wire format: [Length (4)] [Type (1)] [Compression (1)] [Payload (N)]
    /// The type byte is treated as a raw <c>int</c> so that the protocol layer remains
    /// proto-agnostic — it has no knowledge of specific message semantics beyond the two
    /// special framing values <see cref="MessageType.SecureEnv"/> (9) and
    /// <see cref="MessageType.Unknown"/> (0).
    /// </summary>
    internal class ProtocolHandler
    {
        private readonly ILogger _logger;
        private readonly INetworkTelemetryService? _telemetry;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);

        // Wire-value constants for the two types this layer needs to know about.
        private static readonly int WireUnknown  = (int)MessageType.Unknown;   // 0
        private static readonly int WireSecureEnv = (int)MessageType.SecureEnv; // 9

        public ProtocolHandler(ILogger logger, INetworkTelemetryService? telemetry = null)
        {
            _logger = logger;
            _telemetry = telemetry;
        }

        public async Task SendMessageAsync(Stream stream, int type, IMessage message, bool useCompression, CipherState? cipherState, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            // 1. Serialize
            byte[] payloadBytes = message.ToByteArray();
            int originalSize = payloadBytes.Length;
            byte compressionFlag = 0x00;

            // 2. Compress (inner payload)
            if (useCompression && payloadBytes.Length > CompressionHelper.THRESHOLD && type != WireSecureEnv)
            {
                payloadBytes = CompressionHelper.Compress(payloadBytes);
                compressionFlag = 0x01; // Brotli
                
                if (_telemetry != null && originalSize > 0)
                {
                    double ratio = (double)payloadBytes.Length / originalSize;
                    _telemetry.RecordValue(MetricType.CompressionRatio, ratio);
                }
            }

            // 3. Encrypt
            if (cipherState != null)
            {
                using (_telemetry?.StartMetric(MetricType.EncryptionTime))
                {
                    // Inner data layout: [Type (1)] [Compression (1)] [Payload (N)]
                    // Use a pooled buffer to avoid a per-message heap allocation.
                    int encryptInputLen = 2 + payloadBytes.Length;
                    byte[] encryptBuf = System.Buffers.ArrayPool<byte>.Shared.Rent(encryptInputLen);
                    (byte[] ciphertext, byte[] iv, byte[] tag) encrypted;
                    try
                    {
                        encryptBuf[0] = (byte)type;
                        encryptBuf[1] = compressionFlag;
                        Buffer.BlockCopy(payloadBytes, 0, encryptBuf, 2, payloadBytes.Length);
                        encrypted = CryptoHelper.Encrypt(encryptBuf, 0, encryptInputLen, cipherState.EncryptKey);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(encryptBuf);
                    }

                    var env = new SecureEnvelope
                    {
                        Ciphertext = ByteString.CopyFrom(encrypted.ciphertext),
                        Nonce = ByteString.CopyFrom(encrypted.iv),
                        AuthTag = ByteString.CopyFrom(encrypted.tag)
                    };

                    payloadBytes = env.ToByteArray();
                    type = WireSecureEnv;
                    compressionFlag = 0x00; // Outer envelope is not compressed
                }
            }

            // 4. Thread-Safe Write
            await _writeLock.WaitAsync(token);
            try
            {
                _logger.LogDebug("Sending Message {Type}, OrgSize: {Org}, WireSize: {Wire}", type, originalSize, payloadBytes.Length);

                // Framing: [Length (4)] [Type (1)] [Compression (1)] [Payload (N)]
                var lengthBytes = BitConverter.GetBytes(payloadBytes.Length);
                await stream.WriteAsync(lengthBytes, 0, 4, token);
                stream.WriteByte((byte)type);
                stream.WriteByte(compressionFlag);
                await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length, token);
                await stream.FlushAsync(token);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<(int Type, byte[] Payload)> ReadMessageAsync(Stream stream, CipherState? cipherState, CancellationToken token = default)
        {
            await _readLock.WaitAsync(token);
            try
            {
                var lenBuf = new byte[4];
                int read = await ReadExactAsync(stream, lenBuf, 0, 4, token);
                if (read == 0) return (WireUnknown, null!);

                int length = BitConverter.ToInt32(lenBuf, 0);

                int typeByte = stream.ReadByte();
                if (typeByte == -1) throw new EndOfStreamException("Connection closed abruptly (type byte)");

                int compByte = stream.ReadByte();
                if (compByte == -1) throw new EndOfStreamException("Connection closed abruptly (comp byte)");

                var payload = new byte[length];
                await ReadExactAsync(stream, payload, 0, length, token);

                int msgType = typeByte;

                // Handle Secure Envelope
                if (msgType == WireSecureEnv)
                {
                    if (cipherState == null) throw new InvalidOperationException("Received encrypted message but no cipher state established");

                    byte[] decrypted;
                    using (_telemetry?.StartMetric(MetricType.DecryptionTime))
                    {
                        var env = SecureEnvelope.Parser.ParseFrom(payload);
                        decrypted = CryptoHelper.Decrypt(
                            env.Ciphertext.ToByteArray(),
                            env.Nonce.ToByteArray(),
                            env.AuthTag.ToByteArray(),
                            cipherState.DecryptKey);
                    }

                    if (decrypted.Length < 2) throw new InvalidDataException("Decrypted payload too short");

                    msgType = decrypted[0];
                    int innerComp = decrypted[1];

                    var innerPayload = new byte[decrypted.Length - 2];
                    Buffer.BlockCopy(decrypted, 2, innerPayload, 0, innerPayload.Length);

                    if (innerComp == 0x01)
                    {
                        innerPayload = CompressionHelper.Decompress(innerPayload);
                    }

                    return (msgType, innerPayload);
                }

                // Handle Unencrypted Compression
                if (compByte == 0x01)
                {
                    payload = CompressionHelper.Decompress(payload);
                }

                _logger.LogDebug("Read Message {Type}, Size: {Size}", msgType, payload.Length);
                return (msgType, payload);
            }
            finally
            {
                _readLock.Release();
            }
        }

        private async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total, token);
                if (read == 0) return 0; // EOF
                total += read;
            }
            return total;
        }
    }
}
