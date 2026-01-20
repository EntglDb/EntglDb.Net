# Remote Peer Configuration - Automatic Synchronization

## ✅ Automatic Synchronization (Current Implementation)

### Remote Peer List is Automatically Synchronized

As of this implementation, remote peer configurations are stored in a **synchronized collection** (`_system_remote_peers`) that is automatically replicated across all nodes in the cluster through EntglDB's built-in sync infrastructure.

### Why This Matters for Leader Election

When using the `BullyLeaderElectionService`:
1. Any LAN node can be elected as the Cloud Gateway (leader)
2. Only the elected leader connects to remote cloud nodes
3. **All nodes automatically have the same remote peer configurations through sync**
4. Leader election is always effective because all nodes know about all remote peers

### How It Works

1. Remote peer configurations are stored as documents in a special collection: `_system_remote_peers`
2. This collection is synchronized automatically through EntglDB's normal sync process
3. When you add/remove/modify a remote peer on any node, it syncs to all other nodes
4. All nodes maintain consistent remote peer configurations without manual intervention

## 🚀 Simple Deployment Pattern

### Just Add on Any Node

Since configurations sync automatically, you only need to add remote peers on **one node**:

```csharp
// Add on any node in the cluster
var database = new PeerDatabase(store, configProvider);
var peerManagement = new PeerManagementService(database, logger);

await peerManagement.AddCloudPeerAsync(
    "cloud-node-1",
    "remote.entgldb.com:9000",
    new OAuth2Configuration {
        Authority = "https://identity.example.com",
        ClientId = "lan-cluster-client",
        ClientSecret = "secret",
        Scopes = new[] { "entgldb:sync" }
    }
);

// Configuration automatically syncs to all other nodes in cluster
```

### Verification

Query remote peers on any node - they should all return the same list:

```csharp
var peers = await peerManagement.GetAllRemotePeersAsync();
foreach (var peer in peers)
{
    Console.WriteLine($"{peer.NodeId}: {peer.Address} (Type: {peer.Type}, Enabled: {peer.IsEnabled})");
}
```

## 📋 Optional: Configuration File for Initial Setup

You can still use configuration files for initial setup on cluster creation:

**appsettings.json** (optional, for first-time cluster setup):
```json
{
  "EntglDb": {
    "InitialRemotePeers": [
      {
        "nodeId": "cloud-node-1",
        "address": "remote.entgldb.com:9000",
        "type": "CloudRemote",
        "oAuth2": {
          "authority": "https://identity.example.com",
          "clientId": "lan-cluster-client",
          "clientSecret": "${SECRET_FROM_ENV}",
          "scopes": ["entgldb:sync"]
        }
      }
    ]
  }
}
```

**Startup code** (apply once on any node):
```csharp
var initialPeers = configuration.GetSection("EntglDb:InitialRemotePeers")
    .Get<List<RemotePeerConfig>>();

if (initialPeers != null && initialPeers.Any())
{
    var peerManagement = new PeerManagementService(database, logger);
    
    foreach (var remotePeer in initialPeers)
    {
        // Check if already exists to avoid duplicates
        var existing = await peerManagement.GetAllRemotePeersAsync();
        if (!existing.Any(p => p.NodeId == remotePeer.NodeId))
        {
            await peerManagement.AddCloudPeerAsync(
                remotePeer.NodeId,
                remotePeer.Address,
                remotePeer.OAuth2
            );
        }
    }
}
```

## 🔄 Dynamic Management

### Add/Remove Peers at Runtime

You can manage remote peers dynamically without restarting nodes:

```csharp
// Add a new remote peer (syncs to all nodes)
await peerManagement.AddCloudPeerAsync("cloud-2", "cloud2.example.com:9000", oauth2Config);

// Disable a peer temporarily (syncs to all nodes)
await peerManagement.DisablePeerAsync("cloud-1");

// Re-enable later (syncs to all nodes)
await peerManagement.EnablePeerAsync("cloud-1");

// Remove a peer permanently (syncs to all nodes)
await peerManagement.RemoveRemotePeerAsync("cloud-2");
```

All changes are automatically synchronized across the cluster within seconds (depending on sync interval).

## 🚨 Common Use Cases

### Use Case 1: Add Cloud Node to Existing Cluster

**Scenario**: You have a running 3-node LAN cluster and want to add cloud connectivity

**Solution**: Connect to any node and add the cloud peer
```bash
# SSH to any node
ssh node-1

# Add cloud peer via API or code
dotnet run -- add-cloud-peer \
  --node-id cloud-1 \
  --address cloud.example.com:9000 \
  --authority https://identity.example.com \
  --client-id client-id
```

The configuration automatically syncs to all 3 nodes.

### Use Case 2: Temporary Disable Cloud Sync

**Scenario**: Cloud node is under maintenance, disable temporarily

**Solution**: Disable on any node
```csharp
await peerManagement.DisablePeerAsync("cloud-1");
// Syncs to all nodes, leader stops connecting
```

Re-enable when maintenance is done:
```csharp
await peerManagement.EnablePeerAsync("cloud-1");
// Syncs to all nodes, leader resumes connecting
```

### Use Case 3: Replace Cloud Node

**Scenario**: Migrating to a new cloud provider

**Solution**:
```csharp
// Add new cloud node (old one still active)
await peerManagement.AddCloudPeerAsync("cloud-new", "newcloud.com:9000", newOAuth2);

// Disable old cloud node
await peerManagement.DisablePeerAsync("cloud-old");

// After verification, remove old cloud node
await peerManagement.RemoveRemotePeerAsync("cloud-old");
```

All changes sync automatically to all nodes.

## ✅ Benefits of Automatic Synchronization

1. **Zero Configuration Drift**: All nodes always have identical remote peer lists
2. **Dynamic Updates**: Add/remove/modify peers without restarting any node
3. **Consistent Leader Election**: Leader always knows about all remote peers
4. **Simplified Operations**: Manage from any node, changes propagate automatically
5. **High Availability**: No single point of failure for configuration management

## 🔮 Technical Details

### Collection Name
Remote peers are stored in: `_system_remote_peers`

### Synchronization Mechanism
Uses EntglDB's built-in document synchronization:
- Documents are oplog entries with HLC timestamps
- Conflict resolution uses last-write-wins by default
- Synchronization happens every 2 seconds (default sync interval)

### Schema
Each remote peer is a document with NodeId as primary key:
```json
{
  "NodeId": "cloud-node-1",
  "Address": "remote.entgldb.com:9000",
  "Type": 2,
  "OAuth2Json": "{\"authority\":\"https://...\", ...}",
  "IsEnabled": true
}
```

---

**Current Status**: Automatic synchronization implemented  
**Recommended**: Simply add remote peers on any node - they sync automatically  
**No Manual Consistency Required**: Configuration synchronization is automatic and real-time
