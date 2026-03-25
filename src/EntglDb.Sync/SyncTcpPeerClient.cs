using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Network.Telemetry;
using EntglDb.Sync.Proto;

namespace EntglDb.Sync;

/// <summary>
/// Wraps a <see cref="TcpPeerClient"/> with EntglDb sync-specific operations:
/// vector clocks, oplog pull/push, chain-range recovery, and snapshot transfer.
/// </summary>
public class SyncTcpPeerClient : IDisposable
{
    private readonly TcpPeerClient _client;

    public SyncTcpPeerClient(TcpPeerClient client)
    {
        _client = client;
    }

    /// <summary>Gets the list of collections the remote peer declared during handshake.</summary>
    public IReadOnlyList<string> RemoteInterests => _client.RemoteInterests;

    /// <summary>Gets whether the underlying TCP connection is established.</summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Retrieves the remote peer's latest HLC timestamp.
    /// </summary>
    public async Task<HlcTimestamp> GetClockAsync(CancellationToken token)
    {
        using (_client.Telemetry?.StartMetric(MetricType.RoundTripTime))
        {
            await _client.SendProtocolMessageAsync((int)SyncMessageType.GetClockReq, new GetClockRequest(), _client.UseCompression, token);

            var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
            if (type != (int)SyncMessageType.ClockRes) throw new Exception("Unexpected response");

            var res = ClockResponse.Parser.ParseFrom(payload);
            return new HlcTimestamp(res.HlcWall, res.HlcLogic, res.HlcNode);
        }
    }

    /// <summary>
    /// Retrieves the remote peer's vector clock (latest timestamp per node).
    /// </summary>
    public async Task<VectorClock> GetVectorClockAsync(CancellationToken token)
    {
        using (_client.Telemetry?.StartMetric(MetricType.RoundTripTime))
        {
            await _client.SendProtocolMessageAsync((int)SyncMessageType.GetVectorClockReq, new GetVectorClockRequest(), _client.UseCompression, token);

            var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
            if (type != (int)SyncMessageType.VectorClockRes) throw new Exception("Unexpected response");

            var res = VectorClockResponse.Parser.ParseFrom(payload);
            var vectorClock = new VectorClock();

            foreach (var entry in res.Entries)
                vectorClock.SetTimestamp(entry.NodeId, new HlcTimestamp(entry.HlcWall, entry.HlcLogic, entry.NodeId));

            return vectorClock;
        }
    }

    /// <summary>
    /// Pulls oplog changes from the remote peer since the specified timestamp.
    /// </summary>
    public async Task<List<OplogEntry>> PullChangesAsync(HlcTimestamp since, CancellationToken token)
        => await PullChangesAsync(since, null, token);

    /// <summary>
    /// Pulls oplog changes from the remote peer since the specified timestamp, filtered by collections.
    /// </summary>
    public async Task<List<OplogEntry>> PullChangesAsync(HlcTimestamp since, IEnumerable<string>? collections, CancellationToken token)
    {
        var req = new PullChangesRequest
        {
            SinceWall = since.PhysicalTime,
            SinceLogic = since.LogicalCounter,
            SinceNode = since.NodeId
        };
        if (collections != null)
            foreach (var coll in collections)
                req.Collections.Add(coll);

        await _client.SendProtocolMessageAsync((int)SyncMessageType.PullChangesReq, req, _client.UseCompression, token);

        var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
        if (type != (int)SyncMessageType.ChangeSetRes) throw new Exception("Unexpected response");

        return MapOplogEntries(ChangeSetResponse.Parser.ParseFrom(payload).Entries);
    }

    /// <summary>
    /// Pulls oplog changes for a specific node from the remote peer since the specified timestamp.
    /// </summary>
    public async Task<List<OplogEntry>> PullChangesFromNodeAsync(string nodeId, HlcTimestamp since, CancellationToken token)
        => await PullChangesFromNodeAsync(nodeId, since, null, token);

    /// <summary>
    /// Pulls oplog changes for a specific node from the remote peer since the specified timestamp, filtered by collections.
    /// </summary>
    public async Task<List<OplogEntry>> PullChangesFromNodeAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections, CancellationToken token)
    {
        var req = new PullChangesRequest
        {
            SinceNode = nodeId,
            SinceWall = since.PhysicalTime,
            SinceLogic = since.LogicalCounter
        };
        if (collections != null)
            foreach (var coll in collections)
                req.Collections.Add(coll);

        await _client.SendProtocolMessageAsync((int)SyncMessageType.PullChangesReq, req, _client.UseCompression, token);

        var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
        if (type != (int)SyncMessageType.ChangeSetRes) throw new Exception("Unexpected response");

        return MapOplogEntries(ChangeSetResponse.Parser.ParseFrom(payload).Entries);
    }

    /// <summary>
    /// Retrieves a range of oplog entries connecting two hashes (Gap Recovery).
    /// </summary>
    public virtual async Task<List<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken token)
    {
        var req = new GetChainRangeRequest { StartHash = startHash, EndHash = endHash };
        await _client.SendProtocolMessageAsync((int)SyncMessageType.GetChainRangeReq, req, _client.UseCompression, token);

        var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
        if (type != (int)SyncMessageType.ChainRangeRes) throw new Exception($"Unexpected response for ChainRange: {type}");

        var res = ChainRangeResponse.Parser.ParseFrom(payload);
        if (res.SnapshotRequired) throw new SnapshotRequiredException();

        return MapOplogEntries(res.Entries);
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

        await _client.SendProtocolMessageAsync((int)SyncMessageType.PushChangesReq, req, _client.UseCompression, token);

        var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
        if (type != (int)SyncMessageType.AckRes) throw new Exception("Push failed");

        var res = AckResponse.Parser.ParseFrom(payload);
        if (res.SnapshotRequired) throw new SnapshotRequiredException();
        if (!res.Success) throw new Exception("Push failed");
    }

    /// <summary>
    /// Downloads the full database snapshot from the remote peer into <paramref name="destination"/>.
    /// </summary>
    public async Task GetSnapshotAsync(Stream destination, CancellationToken token)
    {
        await _client.SendProtocolMessageAsync((int)SyncMessageType.GetSnapshotReq, new GetSnapshotRequest(), _client.UseCompression, token);

        while (true)
        {
            var (type, payload) = await _client.ReceiveProtocolMessageAsync(token);
            if (type != (int)SyncMessageType.SnapshotChunkMsg) throw new Exception($"Unexpected message type during snapshot: {type}");

            var chunk = SnapshotChunk.Parser.ParseFrom(payload);
            if (chunk.Data.Length > 0)
                await destination.WriteAsync(chunk.Data.ToByteArray(), 0, chunk.Data.Length, token);

            if (chunk.IsLast) break;
        }
    }

    private List<OplogEntry> MapOplogEntries(IEnumerable<ProtoOplogEntry> entries) =>
        entries.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            ParseOp(e.Operation),
            string.IsNullOrEmpty(e.JsonData) ? default : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.JsonData),
            new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
            e.PreviousHash,
            e.Hash
        )).ToList();

    private static OperationType ParseOp(string op) =>
        Enum.TryParse<OperationType>(op, out var val) ? val : OperationType.Put;

    /// <summary>Disposes the underlying <see cref="TcpPeerClient"/> connection.</summary>
    public void Dispose() => _client.Dispose();
}

