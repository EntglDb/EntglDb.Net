using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Network.Proto;
using EntglDb.Network.Security;
using EntglDb.Network.Protocol;
using EntglDb.Network.Telemetry;

namespace EntglDb.Network;

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
    private readonly object _connectionLock = new object();
    private bool _disposed = false;
    
    private const int ConnectionTimeoutMs = 5000;
    private const int OperationTimeoutMs = 30000;

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
    
    public bool HasHandshaked { get; private set; }

    private readonly INetworkTelemetryService? _telemetry;

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
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TcpPeerClient));
            }
            
            if (IsConnected) return;
        }

        var parts = _peerAddress.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid address format: {_peerAddress}. Expected format: host:port");
        }

        if (!int.TryParse(parts[1], out int port) || port <= 0 || port > 65535)
        {
            throw new ArgumentException($"Invalid port number: {parts[1]}");
        }

        // Connect with timeout
        using var timeoutCts = new CancellationTokenSource(ConnectionTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
        
        try
        {
            await _client.ConnectAsync(parts[0], port);
            
            lock (_connectionLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(TcpPeerClient));
                }
                
                _stream = _client.GetStream();
                
                // CRITICAL for Android: Disable Nagle's algorithm to prevent buffering delays
                // This ensures immediate packet transmission for handshake data
                _client.NoDelay = true;
                
                // Configure TCP keepalive
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                
                // Set read/write timeouts
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

        if (CompressionHelper.IsBrotliSupported)
        {
            req.SupportedCompression.Add("brotli");
        }

        _logger.LogDebug("Sending HandshakeReq to {Address}", _peerAddress);
        await _protocol.SendMessageAsync(_stream!, MessageType.HandshakeReq, req, false, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        _logger.LogDebug("Received Handshake response type: {Type}", type);
        
        if (type != MessageType.HandshakeRes) return false;

        var res = HandshakeResponse.Parser.ParseFrom(payload);

        // Negotiation Result
        if (res.SelectedCompression == "brotli")
        {
            _useCompression = true;
            _logger.LogInformation("Brotli compression negotiated.");
        }

        HasHandshaked = res.Accepted;
        return res.Accepted;
    }

    /// <summary>
    /// Retrieves the remote peer's latest HLC timestamp.
    /// </summary>
    public async Task<HlcTimestamp> GetClockAsync(CancellationToken token)
    {
        using (_telemetry?.StartMetric(MetricType.RoundTripTime))
        {
            await _protocol.SendMessageAsync(_stream!, MessageType.GetClockReq, new GetClockRequest(), _useCompression, _cipherState, token);

            var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
            if (type != MessageType.ClockRes) throw new Exception("Unexpected response");

            var res = ClockResponse.Parser.ParseFrom(payload);
            return new HlcTimestamp(res.HlcWall, res.HlcLogic, res.HlcNode);
        }
    }

    /// <summary>
    /// Retrieves the remote peer's vector clock (latest timestamp per node).
    /// </summary>
    public async Task<VectorClock> GetVectorClockAsync(CancellationToken token)
    {
        using (_telemetry?.StartMetric(MetricType.RoundTripTime))
        {
            await _protocol.SendMessageAsync(_stream!, MessageType.GetVectorClockReq, new GetVectorClockRequest(), _useCompression, _cipherState, token);

            var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
            if (type != MessageType.VectorClockRes) throw new Exception("Unexpected response");

            var res = VectorClockResponse.Parser.ParseFrom(payload);
            var vectorClock = new VectorClock();

            foreach (var entry in res.Entries)
            {
                vectorClock.SetTimestamp(entry.NodeId, new HlcTimestamp(entry.HlcWall, entry.HlcLogic, entry.NodeId));
            }

            return vectorClock;
        }
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
        await _protocol.SendMessageAsync(_stream!, MessageType.PullChangesReq, req, _useCompression, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        if (type != MessageType.ChangeSetRes) throw new Exception("Unexpected response");

        var res = ChangeSetResponse.Parser.ParseFrom(payload);

        return res.Entries.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            ParseOp(e.Operation),
            string.IsNullOrEmpty(e.JsonData) ? default : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
            new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
            e.PreviousHash,
            e.Hash // Pass the received hash to preserve integrity reference
        )).ToList();
    }

    /// <summary>
    /// Pulls oplog changes for a specific node from the remote peer since the specified timestamp.
    /// </summary>
    public async Task<List<OplogEntry>> PullChangesFromNodeAsync(string nodeId, HlcTimestamp since, CancellationToken token)
    {
        var req = new PullChangesRequest
        {
            SinceNode = nodeId,
            SinceWall = since.PhysicalTime,
            SinceLogic = since.LogicalCounter
        };
        await _protocol.SendMessageAsync(_stream!, MessageType.PullChangesReq, req, _useCompression, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        if (type != MessageType.ChangeSetRes) throw new Exception("Unexpected response");

        var res = ChangeSetResponse.Parser.ParseFrom(payload);

        return res.Entries.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            ParseOp(e.Operation),
            string.IsNullOrEmpty(e.JsonData) ? default : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
            new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
            e.PreviousHash,
            e.Hash
        )).ToList();
    }

    /// <summary>
    /// Retrieves a range of oplog entries connecting two hashes (Gap Recovery).
    /// </summary>
    public async Task<List<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken token)
    {
        var req = new GetChainRangeRequest { StartHash = startHash, EndHash = endHash };
        await _protocol.SendMessageAsync(_stream!, MessageType.GetChainRangeReq, req, _useCompression, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        if (type != MessageType.ChainRangeRes) throw new Exception($"Unexpected response for ChainRange: {type}");

        var res = ChainRangeResponse.Parser.ParseFrom(payload);

        if (res.SnapshotRequired) throw new SnapshotRequiredException();

        return res.Entries.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            ParseOp(e.Operation),
            string.IsNullOrEmpty(e.JsonData) ? default : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
            new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
            e.PreviousHash,
            e.Hash
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

        foreach (var e in entryList)
        {
            req.Entries.Add(new ProtoOplogEntry
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

        await _protocol.SendMessageAsync(_stream!, MessageType.PushChangesReq, req, _useCompression, _cipherState, token);

        var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
        if (type != MessageType.AckRes) throw new Exception("Push failed");

        var res = AckResponse.Parser.ParseFrom(payload);
        if (res.SnapshotRequired) throw new SnapshotRequiredException();
        if (!res.Success) throw new Exception("Push failed");
    }

    private bool _useCompression = false; // Negotiated after handshake

    private OperationType ParseOp(string op) => Enum.TryParse<OperationType>(op, out var val) ? val : OperationType.Put;

    public async Task GetSnapshotAsync(Stream destination, CancellationToken token)
    {
        await _protocol.SendMessageAsync(_stream!, MessageType.GetSnapshotReq, new GetSnapshotRequest(), _useCompression, _cipherState, token);

        while (true)
        {
             var (type, payload) = await _protocol.ReadMessageAsync(_stream!, _cipherState, token);
             if (type != MessageType.SnapshotChunkMsg) throw new Exception($"Unexpected message type during snapshot: {type}");

             var chunk = SnapshotChunk.Parser.ParseFrom(payload);
             if (chunk.Data.Length > 0)
             {
                 await destination.WriteAsync(chunk.Data.ToByteArray(), 0, chunk.Data.Length, token);
             }

             if (chunk.IsLast) break;
        }
    }

    public void Dispose()
    {
        lock (_connectionLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        
        try
        {
            _stream?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing network stream");
        }
        
        try
        {
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing TCP client");
        }
        
        _logger.LogDebug("Disposed connection to peer: {Address}", _peerAddress);
    }
}

public class SnapshotRequiredException : Exception
{
    public SnapshotRequiredException() : base("Peer requires a full snapshot sync.") { }
}
