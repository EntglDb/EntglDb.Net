using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// TCP server that handles incoming synchronization requests from remote peers.
/// </summary>
internal class TcpSyncServer : ISyncServer
{
    private readonly IPeerStore _store;
    private readonly ILogger<TcpSyncServer> _logger;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private readonly object _startStopLock = new object();
    private int _activeConnections = 0;
    
    private const int MaxConcurrentConnections = 100;
    private const int ClientOperationTimeoutMs = 60000;

    private readonly IAuthenticator _authenticator;

    /// <summary>
    /// Initializes a new instance of the TcpSyncServer class with the specified peer store, configuration provider,
    /// logger, and authenticator.
    /// </summary>
    /// <remarks>The server automatically restarts when the configuration provided by
    /// peerNodeConfigurationProvider changes. This ensures that configuration updates are applied without requiring
    /// manual intervention.</remarks>
    /// <param name="store">The peer store used to manage and persist peer information for the server.</param>
    /// <param name="peerNodeConfigurationProvider">The provider that supplies configuration settings for the peer node and notifies the server of configuration
    /// changes.</param>
    /// <param name="logger">The logger used to record informational and error messages for the server instance.</param>
    /// <param name="authenticator">The authenticator responsible for validating peer connections to the server.</param>
    public TcpSyncServer(
        IPeerStore store, 
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider, 
        ILogger<TcpSyncServer> logger, 
        IAuthenticator authenticator)
    {
        _store = store;
        _logger = logger;
        _authenticator = authenticator;
        _configProvider = peerNodeConfigurationProvider;
        _configProvider.ConfigurationChanged += async (s, e) =>
        {
            _logger.LogInformation("Configuration changed, restarting TCP Sync Server...");
            await Stop();
            await Start();
        };
    }

    /// <summary>
    /// Starts the TCP synchronization server and begins listening for incoming connections asynchronously.
    /// </summary>
    /// <remarks>If the server is already running, this method returns immediately without starting a new
    /// listener. The server will listen on the TCP port specified in the current configuration.</remarks>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    public async Task Start()
    {
        var config = await _configProvider.GetConfiguration();

        lock (_startStopLock)
        {
            if (_cts != null)
            {
                _logger.LogWarning("TCP Sync Server already started");
                return;
            }
            _cts = new CancellationTokenSource();
        }

        _listener = new TcpListener(IPAddress.Any, config.TcpPort);
        _listener.Start();

        _logger.LogInformation("TCP Sync Server Listening on port {Port}", config.TcpPort);

        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await ListenAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TCP Listen task failed");
            }
        }, token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the listener and cancels any pending operations.
    /// </summary>
    /// <remarks>After calling this method, the listener will no longer accept new connections or process
    /// requests. This method is safe to call multiple times; subsequent calls have no effect if the listener is already
    /// stopped.</remarks>
    /// <returns>A task that represents the asynchronous stop operation.</returns>
    public async Task Stop()
    {
        CancellationTokenSource? ctsToDispose = null;
        TcpListener? listenerToStop = null;
        
        lock (_startStopLock)
        {
            if (_cts == null)
            {
                _logger.LogWarning("TCP Sync Server already stopped or never started");
                return;
            }
            
            ctsToDispose = _cts;
            listenerToStop = _listener;
            _cts = null;
            _listener = null;
        }
        
        try
        {
            ctsToDispose.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
        finally
        {
            ctsToDispose.Dispose();
        }
        
        listenerToStop?.Stop();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the full local endpoint on which the server is listening.
    /// </summary>
    public IPEndPoint? ListeningEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <summary>
    /// Gets the port on which the server is listening.
    /// </summary>
    public int? ListeningPort => ListeningEndpoint?.Port;

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
        var remoteEp = client.Client.RemoteEndPoint;
        _logger.LogDebug("Client Connected: {Endpoint}", remoteEp);
        
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // CRITICAL for Android: Disable Nagle's algorithm for immediate packet send
                client.NoDelay = true;
                
                // Configure TCP keepalive
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                
                // Set stream timeouts
                stream.ReadTimeout = ClientOperationTimeoutMs;
                stream.WriteTimeout = ClientOperationTimeoutMs;
                
                bool useCompression = false;

                while (client.Connected && !token.IsCancellationRequested)
                {
                    var config = await _configProvider.GetConfiguration();
                    var (type, payload) = await ReadMessageAsync(stream, token);
                    if (type == MessageType.Unknown) break; // EOF or Error

                    // Handshake Loop
                    if (type == MessageType.HandshakeReq)
                    {
                        var hReq = HandshakeRequest.Parser.ParseFrom(payload);
                        bool valid = await _authenticator.ValidateAsync(hReq.NodeId, hReq.AuthToken);
                        if (!valid)
                        {
                            _logger.LogWarning("Authentication failed for Node {NodeId}", hReq.NodeId);
                            await SendMessageAsync(stream, MessageType.HandshakeRes, new HandshakeResponse { NodeId = config.NodeId, Accepted = false }, false);
                            return;
                        }

                        var hRes = new HandshakeResponse { NodeId = config.NodeId, Accepted = true };
                        if (CompressionHelper.IsBrotliSupported && hReq.SupportedCompression.Contains("brotli"))
                        {
                            hRes.SelectedCompression = "brotli";
                            useCompression = true;
                        }

                        await SendMessageAsync(stream, MessageType.HandshakeRes, hRes, false);
                        continue;
                    }

                    IMessage? response = null;
                    MessageType resType = MessageType.Unknown;

                    switch (type)
                    {
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
                            foreach (var e in oplog)
                            {
                                csRes.Entries.Add(new ProtoOplogEntry
                                {
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

                            await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), entries, token);

                            response = new AckResponse { Success = true };
                            resType = MessageType.AckRes;
                            break;
                    }

                    if (response != null)
                    {
                        await SendMessageAsync(stream, resType, response, useCompression);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Client Handler Error from {Endpoint}: {Message}", remoteEp, ex.Message);
        }
        finally
        {
            _logger.LogDebug("Client Disconnected: {Endpoint}", remoteEp);
        }
    }

    private async Task SendMessageAsync(NetworkStream stream, MessageType type, IMessage message, bool useCompression)
    {
        var payloadBytes = message.ToByteArray();
        byte compressionFlag = 0x00;

        if (useCompression && payloadBytes.Length > CompressionHelper.THRESHOLD)
        {
            payloadBytes = CompressionHelper.Compress(payloadBytes);
            compressionFlag = 0x01;
        }

        // Framing: [Length] [Type] [Comp] [Payload]
        var length = BitConverter.GetBytes(payloadBytes.Length);
        await stream.WriteAsync(length, 0, 4);
        stream.WriteByte((byte)type);
        stream.WriteByte(compressionFlag);
        await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
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

        int compByte = stream.ReadByte();
        if (compByte == -1) return (MessageType.Unknown, null);

        var payload = new byte[length];
        total = 0;
        while (total < length)
        {
            int r = await stream.ReadAsync(payload, total, length - total, token);
            if (r == 0) return (MessageType.Unknown, null);
            total += r;
        }

        if (compByte == 0x01)
        {
            payload = CompressionHelper.Decompress(payload);
        }

        return ((MessageType)typeByte, payload);
    }
}
