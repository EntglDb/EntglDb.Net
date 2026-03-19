using EntglDb.Core.Network;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using EntglDb.Network.Protocol;
using EntglDb.Network.Telemetry;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Carries per-message context into a handler. Implements <see cref="IMessageHandlerContext"/>
    /// so it can be passed directly to any <see cref="INetworkMessageHandler"/> implementation.
    /// </summary>
    private sealed class MessageHandlerContext : IMessageHandlerContext
    {
        public byte[] Payload { get; init; } = Array.Empty<byte>();
        public required NetworkStream Stream { get; init; }
        public required ProtocolHandler Protocol { get; init; }
        public bool UseCompression { get; init; }
        public CipherState? CipherState { get; init; }
        public EndPoint? RemoteEndPoint { get; init; }
        public CancellationToken CancellationToken { get; init; }

        public Task SendMessageAsync(Proto.MessageType type, Google.Protobuf.IMessage message, bool useCompression = false)
            => Protocol.SendMessageAsync(Stream, type, message, useCompression, CipherState, CancellationToken);
    }

    /// <summary>
    /// Delegate for a message handler. Returns the response message and its type.
    /// Handlers that stream their response directly return <c>(null, MessageType.Unknown)</c>.
    /// </summary>
    private delegate Task<(IMessage? Response, MessageType ResponseType)> MessageHandler(MessageHandlerContext ctx);

    private readonly ILocalInterestsProvider? _localInterests;
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
    private readonly IDictionary<MessageType, MessageHandler> _handlerRegistry;

    /// <summary>
    /// Initializes a new instance of the TcpSyncServer class with the specified peer configuration provider,
    /// logger, authenticator, and message handlers.
    /// </summary>
    /// <remarks>The server automatically restarts when the configuration provided by
    /// peerNodeConfigurationProvider changes. This ensures that configuration updates are applied without requiring
    /// manual intervention.</remarks>
    /// <param name="localInterests">Optional provider for the local node's interested collections, used during handshake.</param>
    /// <param name="peerNodeConfigurationProvider">The provider that supplies configuration settings for the peer node and notifies the server of configuration
    /// changes.</param>
    /// <param name="logger">The logger used to record informational and error messages for the server instance.</param>
    /// <param name="authenticator">The authenticator responsible for validating peer connections to the server.</param>
    /// <param name="handshakeService">The service used to perform secure handshake (optional).</param>
    /// <param name="telemetry">Optional telemetry service.</param>
    /// <param name="handlers">
    /// All registered <see cref="INetworkMessageHandler"/> instances injected via DI.
    /// This includes the built-in core handlers registered by <c>AddEntglDbNetwork</c>
    /// as well as any user-defined handlers. When two handlers target the same
    /// <see cref="MessageType"/>, the last one registered takes precedence.
    /// </param>
    public TcpSyncServer(
        ILocalInterestsProvider? localInterests,
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider, 
        ILogger<TcpSyncServer> logger, 
        IAuthenticator authenticator,
        IPeerHandshakeService handshakeService,
        IEnumerable<INetworkMessageHandler> handlers,
        INetworkTelemetryService? telemetry = null)
    {
        _localInterests = localInterests;
        _logger = logger;
        _authenticator = authenticator;
        _handshakeService = handshakeService;
        _configProvider = peerNodeConfigurationProvider;
        _telemetry = telemetry;
        _handlerRegistry = BuildHandlerRegistry(handlers);
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
                List<string> remoteInterests = new();

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
                        
                        // Track remote peer interests
                        remoteInterests = hReq.InterestingCollections.ToList();
                        
                        bool valid = await _authenticator.ValidateAsync(hReq.NodeId, hReq.AuthToken);
                        if (!valid)
                        {
                            _logger.LogWarning("Authentication failed for Node {NodeId}", hReq.NodeId);
                            await protocol.SendMessageAsync(stream, MessageType.HandshakeRes, new HandshakeResponse { NodeId = config.NodeId, Accepted = false }, false, cipherState, token);
                            return;
                        }

                        var hRes = new HandshakeResponse { NodeId = config.NodeId, Accepted = true };
                        
                        // Include local interests in response for push filtering (if a provider is available)
                        if (_localInterests != null)
                        {
                            foreach (var coll in _localInterests.InterestedCollection)
                            {
                                hRes.InterestingCollections.Add(coll);
                            }
                        }

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

                    if (_handlerRegistry.TryGetValue(type, out var handler))
                    {
                        var ctx = new MessageHandlerContext
                        {
                            Payload = payload,
                            Stream = stream,
                            Protocol = protocol,
                            UseCompression = useCompression,
                            CipherState = cipherState,
                            RemoteEndPoint = remoteEp,
                            CancellationToken = token
                        };
                        (response, resType) = await handler(ctx);
                    }
                    else
                    {
                        _logger.LogWarning("Received unsupported message type {MessageType} from {Endpoint}", type, remoteEp);
                        // Close connection on unsupported message type to avoid clients hanging
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

    private IDictionary<MessageType, MessageHandler> BuildHandlerRegistry(IEnumerable<INetworkMessageHandler> handlers)
    {
        var registry = new Dictionary<MessageType, MessageHandler>();

        foreach (var handler in handlers)
        {
            if (registry.ContainsKey(handler.MessageType))
            {
                _logger.LogWarning(
                    "Duplicate INetworkMessageHandler registered for MessageType {MessageType}. The last registered handler will be used.",
                    handler.MessageType);
            }

            // Wrap the INetworkMessageHandler.HandleAsync call to match the internal delegate signature
            registry[handler.MessageType] = ctx => handler.HandleAsync(ctx);
        }

        return registry;
    }
}
