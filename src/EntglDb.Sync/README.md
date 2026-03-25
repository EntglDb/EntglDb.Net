# EntglDb.Sync

Synchronization engine for **EntglDb** — provides oplog, vector clocks, anti-entropy gossip, snapshots, and the P2P service mesh.

## What's Included

- **Oplog**: Append-only, SHA-256 hash-chained journal of local changes
- **Vector Clock**: Causal ordering and divergence detection across nodes
- **Anti-Entropy Gossip**: Periodic exchange of vector clocks with peers to detect and reconcile differences
- **Snapshot Sync**: Fast full-state catch-up for nodes that have been offline or missed too many changes
- **Interest-Aware Sync**: Nodes advertise which collections they care about; only relevant changes are exchanged
- **P2P Service Mesh**: The shared TCP connections can be used for custom application-level protocols (message type ≥ 32)

## Installation

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Sync
dotnet add package EntglDb.Persistence.BLite   # or EntglDb.Persistence.EntityFramework
```

## Quick Start

```csharp
builder.Services
    .AddEntglDbCore()
    .AddEntglDbBLite<MyDbContext, MyDocumentStore>(sp => new MyDbContext("data.blite"))
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>()
    .AddEntglDbSync();          // registers sync handlers + SyncOrchestrator + EntglDbNode
```

## Custom P2P Services

Implement `INetworkMessageHandler` for server-side handling and inject `IPeerMessenger` for outbound calls.
Message types ≥ 32 are reserved for user services.

```csharp
// Server side — handle incoming requests
public class PingHandler : INetworkMessageHandler
{
    public int MessageType => 32;

    public Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var req = PingRequest.Parser.ParseFrom(context.Payload);
        return Task.FromResult<(IMessage?, int)>(
            (new PingResponse { Echo = req.Message }, 33));
    }
}

// Register after AddEntglDbNetwork / AddEntglDbSync
services.AddSingleton<INetworkMessageHandler, PingHandler>();

// Client side — send a request
var (_, payload) = await messenger.SendAndReceiveAsync(
    "192.168.1.10:7000", 32, new PingRequest { Message = "hello" }, ct);
var response = PingResponse.Parser.ParseFrom(payload);
```

## Documentation

- [Full README](https://github.com/EntglDb/EntglDb.Net#readme)
- [Architecture](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/architecture.md)
- [Custom P2P Services](https://github.com/EntglDb/EntglDb.Net#custom-p2p-services)
- [Conflict Resolution](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/conflict-resolution.md)
