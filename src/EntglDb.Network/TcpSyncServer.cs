using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;

namespace EntglDb.Network
{
    /// <summary>
    /// TCP server that handles incoming synchronization requests from remote peers.
    /// </summary>
    public class TcpSyncServer
    {
        private readonly int _port;
        private readonly IPeerStore _store;
        private readonly string _nodeId;
        private readonly ILogger<TcpSyncServer> _logger;
        private CancellationTokenSource? _cts;
        private TcpListener? _listener;

        private readonly IAuthenticator _authenticator;

        public TcpSyncServer(int port, IPeerStore store, string nodeId, ILogger<TcpSyncServer> logger, IAuthenticator authenticator)
        {
            _port = port;
            _store = store;
            _nodeId = nodeId;
            _logger = logger;
            _authenticator = authenticator;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            
             _logger.LogInformation("TCP Sync Server Listening on port {Port}", _port);

            Task.Run(() => ListenAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _cts = null;
        }

        /// <summary>
        /// Gets the full local endpoint on which the server is listening.
        /// </summary>
        public IPEndPoint? ListeningEndpoint => _listener?.LocalEndpoint as IPEndPoint;

        /// <summary>
        /// Gets the port on which the server is listening.
        /// </summary>
        public int ListeningPort => ListeningEndpoint?.Port ?? _port;

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_listener == null) break;
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client, token));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TCP Accept Error");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var remoteEp = client.Client.RemoteEndPoint;
                _logger.LogDebug("Client Connected: {Endpoint}", remoteEp);
                try
                {
                    while (client.Connected && !token.IsCancellationRequested)
                    {
                        var (type, payload) = await ReadMessageAsync(stream, token);
                        if (type == MessageType.Unknown) break; // EOF or Error

                        IMessage? response = null;
                        MessageType resType = MessageType.Unknown;

                        switch (type)
                        {
                            case MessageType.HandshakeReq:
                                var hReq = HandshakeRequest.Parser.ParseFrom(payload);
                                bool valid = await _authenticator.ValidateAsync(hReq.NodeId, hReq.AuthToken);
                                if (!valid)
                                {
                                    _logger.LogWarning("Authentication failed for Node {NodeId}", hReq.NodeId);
                                    await SendMessageAsync(stream, MessageType.HandshakeRes, new HandshakeResponse { NodeId = _nodeId, Accepted = false });
                                    return; // Close connection on auth failure
                                }
                                
                                response = new HandshakeResponse { NodeId = _nodeId, Accepted = true };
                                resType = MessageType.HandshakeRes;
                                break;
                            
                            case MessageType.GetClockReq:
                                var clock = await _store.GetLatestTimestampAsync(token);
                                response = new ClockResponse 
                                { 
                                    HlcWall = clock.PhysicalTime,
                                    HlcLogic = clock.LogicalCounter,
                                    HlcNode = clock.NodeId
                                };
                                resType = MessageType.ClockRes;
                                break;

                            case MessageType.PullChangesReq:
                                var pReq = PullChangesRequest.Parser.ParseFrom(payload);
                                var since = new HlcTimestamp(pReq.SinceWall, pReq.SinceLogic, pReq.SinceNode);
                                var oplog = await _store.GetOplogAfterAsync(since, token);
                                var csRes = new ChangeSetResponse();
                                foreach(var e in oplog)
                                {
                                    csRes.Entries.Add(new ProtoOplogEntry {
                                        Collection = e.Collection,
                                        Key = e.Key,
                                        Operation = e.Operation.ToString(),
                                        JsonData = e.Payload?.GetRawText() ?? "",
                                        HlcWall = e.Timestamp.PhysicalTime,
                                        HlcLogic = e.Timestamp.LogicalCounter,
                                        HlcNode = e.Timestamp.NodeId
                                    });
                                }
                                response = csRes;
                                resType = MessageType.ChangeSetRes;
                                break;

                            case MessageType.PushChangesReq:
                                var pushReq = PushChangesRequest.Parser.ParseFrom(payload);
                                var entries = pushReq.Entries.Select(e => new OplogEntry(
                                    e.Collection,
                                    e.Key,
                                    (OperationType)Enum.Parse(typeof(OperationType), e.Operation),
                                    string.IsNullOrEmpty(e.JsonData) ? (System.Text.Json.JsonElement?)null : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
                                    new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode)
                                ));

                                // Apply changes to the store. 
                                // We pass an empty document list because the store implementation derives document state directly from the Oplog entries.
                                await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), entries, token);
                                
                                response = new AckResponse { Success = true };
                                resType = MessageType.AckRes;
                                break;
                        }

                        if (response != null)
                        {
                            await SendMessageAsync(stream, resType, response);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Client Handler Error: {Message}", ex.Message);
                }
            }
        }

        private async Task SendMessageAsync(NetworkStream stream, MessageType type, IMessage message)
        {
            var bytes = message.ToByteArray();
            var length = BitConverter.GetBytes(bytes.Length);
            await stream.WriteAsync(length, 0, 4);
            stream.WriteByte((byte)type);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private async Task<(MessageType, byte[]?)> ReadMessageAsync(NetworkStream stream, CancellationToken token)
        {
            var lenBuf = new byte[4];
            int total = 0;
            while (total < 4)
            {
                int r = await stream.ReadAsync(lenBuf, total, 4 - total, token);
                if (r == 0) return (MessageType.Unknown, null);
                total += r;
            }
            int length = BitConverter.ToInt32(lenBuf, 0);

            int typeByte = stream.ReadByte();
            if (typeByte == -1) return (MessageType.Unknown, null);

            var payload = new byte[length];
            total = 0;
            while (total < length)
            {
                 int r = await stream.ReadAsync(payload, total, length - total, token);
                 if (r == 0) return (MessageType.Unknown, null);
                 total += r;
            }
            return ((MessageType)typeByte, payload);
        }
    }
}
