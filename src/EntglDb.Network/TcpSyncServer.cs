using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using EntglDb.Network.Protocol;
using EntglDb.Network.Telemetry;
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
    
    internal int MaxConnections = 100;
    private const int ClientOperationTimeoutMs = 60000;

    private readonly IAuthenticator _authenticator;
    private readonly IPeerHandshakeService _handshakeService;
    private readonly INetworkTelemetryService? _telemetry;

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
    /// <param name="handshakeService">The service used to perform secure handshake (optional).</param>
    public TcpSyncServer(
        IPeerStore store, 
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider, 
        ILogger<TcpSyncServer> logger, 
        IAuthenticator authenticator,
        IPeerHandshakeService handshakeService,
        EntglDb.Network.Telemetry.INetworkTelemetryService? telemetry = null)
    {
        _store = store;
        _logger = logger;
        _authenticator = authenticator;
        _handshakeService = handshakeService;
        _configProvider = peerNodeConfigurationProvider;
        _telemetry = telemetry;
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
                
                if (_activeConnections >= MaxConnections)
                {
                    _logger.LogWarning("Max connections reached ({Max}). Rejecting client.", MaxConnections);
                    client.Close();
                    continue;
                }

                Interlocked.Increment(ref _activeConnections);

                _ = Task.Run(async () => 
                {
                    try
                    {
                        await HandleClientAsync(client, token);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeConnections);
                    }
                }, token);
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
                
                var protocol = new ProtocolHandler(_logger, _telemetry);
                
                bool useCompression = false;
                CipherState? cipherState = null;

                // Perform Secure Handshake (if service is available)
                var config = await _configProvider.GetConfiguration();
                if (_handshakeService != null)
                {
                    try
                    {
                        // We are NOT initiator
                        _logger.LogDebug("Starting Secure Handshake as Responder.");
                        cipherState = await _handshakeService.HandshakeAsync(stream, false, config.NodeId, token);
                        _logger.LogDebug("Secure Handshake Completed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Secure Handshake failed check logic");
                        return;
                    }
                }

                while (client.Connected && !token.IsCancellationRequested)
                {
                    // Re-fetch config if needed, though usually stable
                    config = await _configProvider.GetConfiguration();

                    var (type, payload) = await protocol.ReadMessageAsync(stream, cipherState, token);
                    if (type == MessageType.Unknown) break; // EOF or Error

                    // Handshake Loop
                    if (type == MessageType.HandshakeReq)
                    {
                        var hReq = HandshakeRequest.Parser.ParseFrom(payload);
                        _logger.LogDebug("Received HandshakeReq from Node {NodeId}", hReq.NodeId);
                        bool valid = await _authenticator.ValidateAsync(hReq.NodeId, hReq.AuthToken);
                        if (!valid)
                        {
                            _logger.LogWarning("Authentication failed for Node {NodeId}", hReq.NodeId);
                            await protocol.SendMessageAsync(stream, MessageType.HandshakeRes, new HandshakeResponse { NodeId = config.NodeId, Accepted = false }, false, cipherState, token);
                            return;
                        }

                        var hRes = new HandshakeResponse { NodeId = config.NodeId, Accepted = true };
                        if (CompressionHelper.IsBrotliSupported && hReq.SupportedCompression.Contains("brotli"))
                        {
                            hRes.SelectedCompression = "brotli";
                            useCompression = true;
                        }

                        await protocol.SendMessageAsync(stream, MessageType.HandshakeRes, hRes, false, cipherState, token);
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

                        case MessageType.GetVectorClockReq:
                            var vectorClock = await _store.GetVectorClockAsync(token);
                            var vcRes = new VectorClockResponse();
                            foreach (var nodeId in vectorClock.NodeIds)
                            {
                                var ts = vectorClock.GetTimestamp(nodeId);
                                vcRes.Entries.Add(new VectorClockEntry
                                {
                                    NodeId = nodeId,
                                    HlcWall = ts.PhysicalTime,
                                    HlcLogic = ts.LogicalCounter
                                });
                            }
                            response = vcRes;
                            resType = MessageType.VectorClockRes;
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
                                    HlcNode = e.Timestamp.NodeId,
                                    Hash = e.Hash,
                                    PreviousHash = e.PreviousHash
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
                                new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
                                e.PreviousHash, // Restore PreviousHash
                                e.Hash          // Restore Hash
                            ));

                            await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), entries, token);

                            response = new AckResponse { Success = true };
                            resType = MessageType.AckRes;
                            break;

                        case MessageType.GetChainRangeReq:
                            var rangeReq = GetChainRangeRequest.Parser.ParseFrom(payload);
                            var rangeEntries = await _store.GetChainRangeAsync(rangeReq.StartHash, rangeReq.EndHash, token);
                            var rangeRes = new ChainRangeResponse();
                            
                            if (!rangeEntries.Any() && rangeReq.StartHash != rangeReq.EndHash)
                            {
                                // Gap cannot be filled (likely pruned or unknown branch)
                                rangeRes.SnapshotRequired = true;
                            }
                            else
                            {
                                foreach (var e in rangeEntries)
                                {
                                    rangeRes.Entries.Add(new ProtoOplogEntry
                                    {
                                        Collection = e.Collection,
                                        Key = e.Key,
                                        Operation = e.Operation.ToString(),
                                        JsonData = e.Payload?.GetRawText() ?? "",
                                        HlcWall = e.Timestamp.PhysicalTime,
                                        HlcLogic = e.Timestamp.LogicalCounter,
                                        HlcNode = e.Timestamp.NodeId,
                                        Hash = e.Hash,
                                        PreviousHash = e.PreviousHash
                                    });
                                }
                            }
                            response = rangeRes;
                            resType = MessageType.ChainRangeRes;
                            break;

                        case MessageType.GetSnapshotReq:
                            _logger.LogInformation("Processing GetSnapshotReq from {Endpoint}", remoteEp);
                            var tempFile = Path.GetTempFileName();
                            try 
                            {
                                // Create backup
                                using (var fs = File.Create(tempFile))
                                {
                                    await _store.CreateSnapshotAsync(fs, token);
                                }
                                
                                using (var fs = File.OpenRead(tempFile))
                                {
                                    byte[] buffer = new byte[80 * 1024]; // 80KB chunks
                                    int bytesRead;
                                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                                    {
                                        var chunk = new SnapshotChunk 
                                        { 
                                            Data = ByteString.CopyFrom(buffer, 0, bytesRead),
                                            IsLast = false 
                                        };
                                        await protocol.SendMessageAsync(stream, MessageType.SnapshotChunkMsg, chunk, false, cipherState, token);
                                    }
                                    
                                    // Send End of Snapshot
                                    await protocol.SendMessageAsync(stream, MessageType.SnapshotChunkMsg, new SnapshotChunk { IsLast = true }, false, cipherState, token);
                                }
                            }
                            finally
                            {
                                if (File.Exists(tempFile)) File.Delete(tempFile);
                            }
                            break;
                    }

                    if (response != null)
                    {
                        await protocol.SendMessageAsync(stream, resType, response, useCompression, cipherState, token);
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
}
