# EntglDb

**EntglDb** (formerly PeerDb) is a lightweight, embeddable Peer-to-Peer (P2P) database for .NET.  
It allows you to build decentralized applications where every node has a local database that synchronizes automatically with other peers in the mesh.

> **🏠 Designed for Local Area Networks (LAN)**  
> EntglDb is built for **trusted LAN environments** (offices, homes, local networks). It is **cross-platform** (Windows, Linux, macOS) and optimized for local network deployments. It is **NOT** designed for public internet use without additional security measures.

## Features
- **Mesh Networking**: Nodes discover each other automatically via UDP Broadcast or Gossip.
- **Offline First**: All data is local. Reads and writes work without connection.
- **Eventual Consistency**: Updates propagate exponentially through the network.
- **Conflict Resolution**: Uses Hybrid Logical Clocks (HLC) aka "Last Write Wins".
- **Scalable**: Supports "Gossip Sync" to handle hundreds of nodes with constant overhead.
- **Cross-Platform**: Runs on Windows, Linux, and macOS (.NET 10).

## Quick Start

### Installation
```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

### Usage
```csharp
// 1. Storage
var store = new SqlitePeerStore("my-node.db");
await store.InitializeAsync();

// 2. Network
var host = new UdpDiscoveryService(nodeId, tcpPort, logger);
var network = new TcpSyncServer(tcpPort, store, discovery, logger);

// 3. Orchestrator
var syncer = new SyncOrchestrator(network, network, store, logger);

// 4. Start
host.Start();
network.Start();
syncer.Start();
```
