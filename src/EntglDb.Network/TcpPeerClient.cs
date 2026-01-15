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
        private NetworkStream? _stream;

        public bool IsConnected => _client != null && _client.Connected && _stream != null;
        public bool HasHandshaked { get; private set; }

        public TcpPeerClient(string peerAddress, ILogger logger)
        {
            _client = new TcpClient();
            _peerAddress = peerAddress;
            _logger = logger;
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
            
            var bytes = message.ToByteArray();
            var length = BitConverter.GetBytes(bytes.Length);
            // Protocol: [Length (4)] [Type (1)] [Payload (N)]
            
            await _stream.WriteAsync(length, 0, 4);
            _stream.WriteByte((byte)type);
            await _stream.WriteAsync(bytes, 0, bytes.Length);
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
            
            return ((MessageType)typeByte, payload);
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
