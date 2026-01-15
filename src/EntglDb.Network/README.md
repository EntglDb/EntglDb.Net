# EntglDb.Network

Networking layer for **EntglDb** - provides peer-to-peer mesh networking with automatic discovery and synchronization.

## What's Included

This package handles all networking for EntglDb:

- **UDP Discovery**: Automatic peer discovery on LAN via broadcast
- **TCP Synchronization**: Reliable data sync between nodes
- **Gossip Protocol**: Efficient update propagation
- **Sync Orchestrator**: Manages peer connections and sync operations
- **Anti-Entropy**: Automatic reconciliation between peers
- **Resilience**: Retry policies, timeouts, error handling

## Installation

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

## Quick Start

```csharp
using EntglDb.Network;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register networking
services.AddEntglDbNetwork(
    nodeId: "my-node",
    tcpPort: 5000,
    authToken: "shared-secret"
);

var provider = services.BuildServiceProvider();

// Start network node
var node = provider.GetRequiredService<EntglDbNode>();
node.Start();

// Nodes on the same LAN will discover each other automatically!
```

## Features

### Automatic Discovery
Nodes broadcast their presence via UDP and automatically connect to peers on the same network.

### Secure Synchronization
All nodes must share the same authentication token to sync data.

### Scalable Gossip
Updates propagate exponentially - each node tells multiple peers, ensuring fast network-wide propagation.

## Documentation

- **[Architecture](https://github.com/lucafabbri/EntglDb/blob/main/docs/architecture.md)**
- **[LAN Deployment](https://github.com/lucafabbri/EntglDb/blob/main/docs/deployment-lan.md)**
- **[Network Configuration](https://github.com/lucafabbri/EntglDb/blob/main/docs/production-hardening.md)**

## Related Packages

- **EntglDb.Core** - Core database abstractions
- **EntglDb.Persistence.Sqlite** - SQLite storage provider

## License

MIT - see [LICENSE](https://github.com/lucafabbri/EntglDb/blob/main/LICENSE)
