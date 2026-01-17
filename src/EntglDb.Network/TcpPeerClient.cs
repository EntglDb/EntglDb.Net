using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;

namespace EntglDb.Network
{
    /// <summary>
    /// Represents a TCP client connection to a remote peer for synchronization.
    /// </summary>
    public class TcpPeerClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly string _peerAddress;
        private readonly ILogger _logger;
        private readonly IPeerHandshakeService? _handshakeService;
        private NetworkStream? _stream;
        private CipherState? _cipherState;

        public bool IsConnected => _client != null && _client.Connected && _stream != null;
        public bool HasHandshaked { get; private set; }

        public TcpPeerClient(string peerAddress, ILogger logger, IPeerHandshakeService? handshakeService = null)
        {
            _client = new TcpClient();
            _peerAddress = peerAddress;
            _logger = logger;
            _handshakeService = handshakeService;
        }

        public async Task ConnectAsync(CancellationToken token)
        {
            if (IsConnected) return;
            
            var parts = _peerAddress.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Invalid address format");
            
            await _client.ConnectAsync(parts[0], int.Parse(parts[1])); 
            _stream = _client.GetStream();
        }

        /// <summary>
        /// Performs authentication handshake with the remote peer.
        /// </summary>
        /// <param name="myNodeId">The local node identifier.</param>
        /// <param name="authToken">The authentication token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if handshake was accepted, false otherwise.</returns>
        public async Task<bool> HandshakeAsync(string myNodeId, string authToken, CancellationToken token)
        {
            if (HasHandshaked) return true;

            if (_handshakeService != null)
            {
                // Perform secure handshake if service is available
                // We assume we are initiator here
                _cipherState = await _handshakeService.HandshakeAsync(_stream!, true, myNodeId, token);
            }

            var req = new HandshakeRequest { NodeId = myNodeId, AuthToken = authToken ?? "" };
            await SendMessageAsync(MessageType.HandshakeReq, req);
            
            var (type, payload) = await ReadMessageAsync(token);
            if (type != MessageType.HandshakeRes) return false;
            
            var res = HandshakeResponse.Parser.ParseFrom(payload);
            HasHandshaked = res.Accepted;
            return res.Accepted;
        }

        /// <summary>
        /// Retrieves the remote peer's latest HLC timestamp.
        /// </summary>
        public async Task<HlcTimestamp> GetClockAsync(CancellationToken token)
        {
            await SendMessageAsync(MessageType.GetClockReq, new GetClockRequest());
            
            var (type, payload) = await ReadMessageAsync(token);
            if (type != MessageType.ClockRes) throw new Exception("Unexpected response");

            var res = ClockResponse.Parser.ParseFrom(payload);
            return new HlcTimestamp(res.HlcWall, res.HlcLogic, res.HlcNode);
        }

        /// <summary>
        /// Pulls oplog changes from the remote peer since the specified timestamp.
        /// </summary>
        public async Task<List<OplogEntry>> PullChangesAsync(HlcTimestamp since, CancellationToken token)
        {
            var req = new PullChangesRequest 
            { 
                SinceWall = since.PhysicalTime,
                SinceLogic = since.LogicalCounter,
                SinceNode = since.NodeId 
            };
            await SendMessageAsync(MessageType.PullChangesReq, req);

            var (type, payload) = await ReadMessageAsync(token);
            if (type != MessageType.ChangeSetRes) throw new Exception("Unexpected response");

            var res = ChangeSetResponse.Parser.ParseFrom(payload);
            
            return res.Entries.Select(e => new OplogEntry(
                e.Collection,
                e.Key,
                ParseOp(e.Operation),
                string.IsNullOrEmpty(e.JsonData) ? default : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
                new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode)
            )).ToList();
        }

        /// <summary>
        /// Pushes local oplog changes to the remote peer.
        /// </summary>
        public async Task PushChangesAsync(IEnumerable<OplogEntry> entries, CancellationToken token)
        {
            var req = new PushChangesRequest();
            var entryList = entries.ToList();
            if (entryList.Count == 0) return;

            foreach(var e in entryList)
            {
                req.Entries.Add(new ProtoOplogEntry {
                    Collection = e.Collection,
                    Key = e.Key,
                    Operation = e.Operation.ToString(),
                    JsonData = e.Payload?.GetRawText() ?? "",
                    HlcWall = e.Timestamp.PhysicalTime,
                    HlcLogic = e.Timestamp.LogicalCounter,
                    HlcNode = e.Timestamp.NodeId
                });
            }

            await SendMessageAsync(MessageType.PushChangesReq, req);
            
            var (type, payload) = await ReadMessageAsync(token);
            if (type != MessageType.AckRes) throw new Exception("Push failed");
        }

        private async Task SendMessageAsync(MessageType type, IMessage message)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");
            
            byte[] payloadBytes = message.ToByteArray();

            if (_cipherState != null)
            {
                // Encrypt payload
                // New Wire format for Encrypted: [Length] [Type=SecureEnv] [SecureEnvelopeBytes]
                // SecureEnvelope contains the actual Type? No, the inner message is just bytes.
                // We need to know the inner type.
                // Protocol update rationale:
                // We wrap the *Original* message (Type + Payload) ? 
                // Or just the payload? If just payload, the outer Type is still visible? 
                // "Payload Security: Business messages ... must be encrypted"
                // The prompt says: "Encrypted Protobuf Envelope ... acts as a container ... ciphertext ... message must be serialized to bytes first"
                // The outer framing is [Length] [Type].
                // If we use Type=SecureEnv, the receiver knows to decrypt.
                // But inside, we need to know the *Original* type.
                // So we should encrypt [Type byte] + [Payload].
                
                var dataToEncrypt = new byte[1 + payloadBytes.Length];
                dataToEncrypt[0] = (byte)type;
                Buffer.BlockCopy(payloadBytes, 0, dataToEncrypt, 1, payloadBytes.Length);
                
                var (ciphertext, iv, tag) = CryptoHelper.Encrypt(dataToEncrypt, _cipherState.EncryptKey);
                
                var env = new SecureEnvelope 
                { 
                    Ciphertext = ByteString.CopyFrom(ciphertext),
                    Nonce = ByteString.CopyFrom(iv),
                    AuthTag = ByteString.CopyFrom(tag)
                };
                
                payloadBytes = env.ToByteArray();
                type = MessageType.SecureEnv;
            }

             var length = BitConverter.GetBytes(payloadBytes.Length);
            
            await _stream.WriteAsync(length, 0, 4);
            _stream.WriteByte((byte)type);
            await _stream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
        }

        private async Task<(MessageType, byte[])> ReadMessageAsync(CancellationToken token)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");

            var lenBuf = new byte[4];
            int read = await ReadExactAsync(lenBuf, 0, 4, token);
            if (read == 0) throw new Exception("Connection closed");
            
            int length = BitConverter.ToInt32(lenBuf, 0);
            
            int typeByte = _stream.ReadByte();
            if (typeByte == -1) throw new Exception("Connection closed");
            
            var payload = new byte[length];
            await ReadExactAsync(payload, 0, length, token);
            
            var msgType = (MessageType)typeByte;

            if (msgType == MessageType.SecureEnv)
            {
                if (_cipherState == null) throw new Exception("Received encrypted message but no cipher state established");
                
                var env = SecureEnvelope.Parser.ParseFrom(payload);
                var decrypted = CryptoHelper.Decrypt(
                    env.Ciphertext.ToByteArray(), 
                    env.Nonce.ToByteArray(), 
                    env.AuthTag.ToByteArray(), 
                    _cipherState.DecryptKey);
                
                // Decrypted data format: [Type (1)] + [Payload (N)]
                if (decrypted.Length < 1) throw new Exception("Decrypted payload too short");
                
                msgType = (MessageType)decrypted[0];
                var innerPayload = new byte[decrypted.Length - 1];
                Buffer.BlockCopy(decrypted, 1, innerPayload, 0, innerPayload.Length);
                
                return (msgType, innerPayload);
            }

            return (msgType, payload);
        }

        private async Task<int> ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int read = await _stream!.ReadAsync(buffer, offset + total, count - total, token);
                if (read == 0) return 0; // EOF
                total += read;
            }
            return total;
        }

        private OperationType ParseOp(string op) => Enum.TryParse<OperationType>(op, out var val) ? val : OperationType.Put;

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
