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

    public TcpPeerClient(string peerAddress, ILogger logger, IPeerHandshakeService? handshakeService = null)
    {
        _client = new TcpClient();
        _peerAddress = peerAddress;
        _logger = logger;
        _handshakeService = handshakeService;
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

        await SendMessageAsync(MessageType.HandshakeReq, req);

        var (type, payload) = await ReadMessageAsync(token);
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
                HlcNode = e.Timestamp.NodeId
            });
        }

        await SendMessageAsync(MessageType.PushChangesReq, req);

        var (type, payload) = await ReadMessageAsync(token);
        if (type != MessageType.AckRes) throw new Exception("Push failed");
    }

    private bool _useCompression = false; // Negotiated after handshake

    private async Task SendMessageAsync(MessageType type, IMessage message)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        byte[] payloadBytes = message.ToByteArray();
        byte compressionFlag = 0x00; // None

        // 1. Compress (if enabled and large)
        // Note: We don't compress Handshake/SecureEnv messages themselves, only the inner payload if feasible.
        // But here 'type' is the logical type.
        // If we are about to Encrypt, we compress FIRST.

        if (_useCompression && payloadBytes.Length > CompressionHelper.THRESHOLD && type != MessageType.SecureEnv)
        {
            payloadBytes = CompressionHelper.Compress(payloadBytes);
            compressionFlag = 0x01; // Brotli
        }

        // 2. Encrypt
        if (_cipherState != null)
        {
            // Inner format: [Type (1)] [Compression (1)] [Payload (N)]
            var dataToEncrypt = new byte[2 + payloadBytes.Length];
            dataToEncrypt[0] = (byte)type;
            dataToEncrypt[1] = compressionFlag;
            Buffer.BlockCopy(payloadBytes, 0, dataToEncrypt, 2, payloadBytes.Length);

            var (ciphertext, iv, tag) = CryptoHelper.Encrypt(dataToEncrypt, _cipherState.EncryptKey);

            var env = new SecureEnvelope
            {
                Ciphertext = ByteString.CopyFrom(ciphertext),
                Nonce = ByteString.CopyFrom(iv),
                AuthTag = ByteString.CopyFrom(tag)
            };

            payloadBytes = env.ToByteArray();
            type = MessageType.SecureEnv;
            compressionFlag = 0x00; // Outer envelope is not compressed
        }

        // 3. Framing: [Length (4)] [Type (1)] [Compression (1)] [Payload (N)]
        // We are adding Compression byte to the wire frame as well for symmetry/unencrypted scenarios.

        var length = BitConverter.GetBytes(payloadBytes.Length);

        await _stream.WriteAsync(length, 0, 4);
        _stream.WriteByte((byte)type);
        _stream.WriteByte(compressionFlag); // v0.7.0 addition
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

        int compByte = _stream.ReadByte(); // v0.7.0
        if (compByte == -1) throw new Exception("Connection closed (missing comp flag)");

        var payload = new byte[length];
        await ReadExactAsync(payload, 0, length, token);

        var msgType = (MessageType)typeByte;

        // Handle Secure Envelope
        if (msgType == MessageType.SecureEnv)
        {
            if (_cipherState == null) throw new Exception("Received encrypted message but no cipher state established");

            var env = SecureEnvelope.Parser.ParseFrom(payload);
            var decrypted = CryptoHelper.Decrypt(
                env.Ciphertext.ToByteArray(),
                env.Nonce.ToByteArray(),
                env.AuthTag.ToByteArray(),
                _cipherState.DecryptKey);

            // Decrypted data format: [Type (1)] [Compression (1)] [Payload (N)]
            if (decrypted.Length < 2) throw new Exception("Decrypted payload too short");

            msgType = (MessageType)decrypted[0];
            int innerComp = decrypted[1];

            var innerPayload = new byte[decrypted.Length - 2];
            Buffer.BlockCopy(decrypted, 2, innerPayload, 0, innerPayload.Length);

            // Decompress inner payload if needed
            if (innerComp == 0x01)
            {
                innerPayload = CompressionHelper.Decompress(innerPayload);
            }

            return (msgType, innerPayload);
        }

        // Handle Unencrypted Compression (unlikely for business data but possible for handshake/errors)
        if (compByte == 0x01)
        {
            payload = CompressionHelper.Decompress(payload);
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
