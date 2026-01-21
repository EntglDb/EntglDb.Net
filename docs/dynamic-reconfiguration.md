---
layout: default
title: Dynamic Reconfiguration
nav_order: 8
---

# Dynamic Reconfiguration & Leader Election

EntglDb v0.8.0 introduces support for dynamic reconfiguration, allowing nodes to change their role, listening ports, and database identity without a full process restart. This is essential for containerized environments and long-running services.

## Configuration Provider

The core of this feature is the `IPeerNodeConfigurationProvider` interface (available in .Net, Kotlin, and NodeJs). 

```typescript
// NodeJs Example
interface IPeerNodeConfigurationProvider {
    getConfiguration(): Promise<PeerNodeConfiguration>;
    onConfigurationChanged(callback: (config: PeerNodeConfiguration) => void): void;
}
```

Implementations can watch a file (e.g., `config.json`), a remote configuration server, or environment variables. When the configuration changes, the provider fires an event.

### Usage

Services like `TcpSyncServer` subscribe to these events.
- **Port Change**: The server stops the listener and restarts on the new port.
- **NodeId Change**: If the NodeID changes, it typically implies a change of identity. The persistence layer (SQLite) is bound to a specific NodeID (internal HLC history). Thus, a NodeID change requires closing the existing DB and opening/creating a new one matching the new ID.

## Known Peers (Static Remote)

You can now persist a list of "Known Peers" (Static Remote nodes) that survive restarts. This is useful for:
- Configuring a fixed set of Cloud Gateways.
- Connecting to specific peers outside the local broadcast domain.

### API

```typescript
// Add a static peer
await peerStore.saveRemotePeer({
    nodeId: "gateway-1",
    address: "192.168.1.50:25000",
    type: PeerType.StaticRemote,
    ...
});

// Retrieve
const peers = await peerStore.getRemotePeers();
```

## Leader Election (Bully Algorithm)

For Cloud Synchronization, usually one node in a LAN cluster acts as the "Gateway" to the cloud to avoid redundant traffic. EntglDb implements the **Bully Algorithm** for automatic leader election.

- **Rule**: Among all discovered peers (LAN or Static) that are eligible, the node with the **lexicographically smallest NodeID** declares itself the Leader.
- **Mechanism**: Each node periodically checks the list of active peers. If it finds no other peer with a smaller ID than itself, it assumes leadership.
- **Event**: The `LeaderElectionService` emits a `LeadershipChanged` event (boolean `isLeader`).
- **Action**: The `SyncOrchestrator` uses this flag to decide whether to push/pull from Cloud Remotes.

### NodeJs Logic
```typescript
const amILeader = !activePeers.some(p => p.nodeId < myNodeId);
```

This simple consensus mechanism works well for small-to-medium clusters on a low-latency LAN.
