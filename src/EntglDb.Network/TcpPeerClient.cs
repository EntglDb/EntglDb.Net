using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using EntglDb.Network.Protocol;
using EntglDb.Network.Telemetry;

namespace EntglDb.Network;

/// <summary>
/// Base class for a TCP client connection to a remote peer.
/// Handles connect, handshake, and generic custom message send/receive.
/// Extend this class to add protocol-specific methods (e.g. <c>SyncTcpPeerClient</c>).
/// </summary>
public class TcpPeerClient : IDisposable
{
    private readonly TcpClient _client;
    private readonly string _peerAddress;
    private readonly IPeerHandshakeService? _handshakeService;
    private readonly object _connectionLock = new object();
    private bool _disposed = false;

    private const int ConnectionTimeoutMs = 5000;
    private const int OperationTimeoutMs = 30000;

    // Protected so subclasses can use the connection state for their own messages.
    protected readonly ILogger _logger;
    protected readonly INetworkTelemetryService? _telemetry;
    protected NetworkStream? _stream;
    protected CipherState? _cipherState;
    protected bool _useCompression = false;
    protected List<string> _remoteInterests = new();

    /// <summary>Gets the telemetry service for use by wrapper classes.</summary>
    public INetworkTelemetryService? Telemetry => _telemetry;

    /// <summary>Gets whether Brotli compression was negotiated with the remote peer.</summary>
    public bool UseCompression => _useCompression;

    // Private: ProtocolHandler is internal — wrappers use the public helpers below.
    private readonly ProtocolHandler _protocol;

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _client != null && _client.Connected && _stream != null && !_disposed;
            }
        }
    }

    public bool HasHandshaked { get; protected set; }

    /// <summary>
    /// Gets the list of collections the remote peer declared as interesting during handshake.
    /// </summary>
    public IReadOnlyList<string> RemoteInterests => _remoteInterests.AsReadOnly();

    public TcpPeerClient(string peerAddress, ILogger logger, IPeerHandshakeService? handshakeService = null, INetworkTelemetryService? telemetry = null)
    {
        _client = new TcpClient();
        _peerAddress = peerAddress;
        _logger = logger;
        _handshakeService = handshakeService;
        _telemetry = telemetry;
        _protocol = new ProtocolHandler(logger, telemetry);
    }

    public async Task ConnectAsync(CancellationToken token)
    {
        lock (_connectionLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TcpPeerClient));
            if (IsConnected) return;
        }

        var parts = _peerAddress.Split(':');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid address format: {_peerAddress}. Expected format: host:port");

        if (!int.TryParse(parts[1], out int port) || port <= 0 || port > 65535)
            throw new ArgumentException($"Invalid port number: {parts[1]}");

        using var timeoutCts = new CancellationTokenSource(ConnectionTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

        try
        {
            await _client.ConnectAsync(parts[0], port);

            lock (_connectionLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TcpPeerClient));

                _stream = _client.GetStream();

                // CRITICAL for Android: Disable Nagle's algorithm to prevent buffering delays
                _client.NoDelay = true;

                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                _stream.ReadTimeout = OperationTimeoutMs;
                _stream.WriteTimeout = OperationTimeoutMs;
            }

            _logger.LogDebug("Connected to peer: {Address} (NoDelay=true for immediate send)", _peerAddress);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Connection to {_peerAddress} timed out after {ConnectionTimeoutMs}ms");
        }
    }

    /// <summary>
    /// Performs the authentication handshake with the remote peer.
    /// </summary>
    public async Task<bool> HandshakeAsync(string myNodeId, string authToken, CancellationToken token)
        => await HandshakeAsync(myNodeId, authToken, null, token);

    /// <summary>
    /// Performs the authentication handshake with the remote peer, declaring collection interests.
    /// </summary>
    public async Task<bool> HandshakeAsync(string myNodeId, string authToken, IEnumerable<string>? interestingCollections, CancellationToken token)
    {
        if (HasHandshaked) return true;

        if (_handshakeService != null)
            _cipherState = await _handshakeService.HandshakeAsync(_stream!, true, myNodeId, token);

        var req = new HandshakeRequest { NodeId = myNodeId, AuthToken = authToken ?? "" };

        if (interestingCollections != null)
            foreach (var coll in interestingCollections)
                req.InterestingCollections.Add(coll);

        if (CompressionHelper.IsBrotliSupported)
            req.SupportedCompression.Add("brotli");

        _logger.LogDebug("Sending HandshakeReq to {Address}", _peerAddress);
        await _protocol.SendMessageAsync(_stream!, (int)MessageType.HandshakeReq, req, false, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        _logger.LogDebug("Received Handshake response type: {Type}", type);

        if (type != (int)MessageType.HandshakeRes) return false;

        var res = HandshakeResponse.Parser.ParseFrom(payload);

        _remoteInterests = new List<string>(res.InterestingCollections);

        if (res.SelectedCompression == "brotli")
        {
            _useCompression = true;
            _logger.LogInformation("Brotli compression negotiated.");
        }

        HasHandshaked = res.Accepted;
        return res.Accepted;
    }

    /// <summary>
    /// Sends a custom message to the connected peer without waiting for a response.
    /// Suitable for fire-and-forget or when the response is read separately via <see cref="ReceiveAsync"/>.
    /// </summary>
    public async Task SendCustomAsync(int messageType, IMessage message, CancellationToken token = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to peer.");
        await _protocol.SendMessageAsync(_stream!, messageType, message, _useCompression, _cipherState, token);
    }

    /// <summary>
    /// Reads the next message from the connected peer.
    /// </summary>
    public async Task<(int MessageType, byte[] Payload)> ReceiveAsync(CancellationToken token = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to peer.");
        return await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
    }

    /// <summary>
    /// Sends a message through the established connection.
    /// Available to subclasses and composition wrappers in other assemblies.
    /// </summary>
    public Task SendProtocolMessageAsync(int type, IMessage message, bool useCompression, CancellationToken token)
        => _protocol.SendMessageAsync(_stream!, type, message, useCompression, _cipherState, token);

    /// <summary>
    /// Reads the next message through the established connection.
    /// Available to subclasses and composition wrappers in other assemblies.
    /// </summary>
    public Task<(int Type, byte[] Payload)> ReceiveProtocolMessageAsync(CancellationToken token)
        => _protocol.ReadMessageAsync(_stream!, _cipherState, token);

    public void Dispose()
    {
        lock (_connectionLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try { _stream?.Dispose(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing network stream"); }

        try { _client?.Dispose(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing TCP client"); }

        _logger.LogDebug("Disposed connection to peer: {Address}", _peerAddress);
    }
}
