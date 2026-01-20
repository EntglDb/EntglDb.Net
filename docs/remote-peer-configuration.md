# Remote Peer Configuration - Deployment Guide

## ⚠️ Important: Configuration Consistency

### Remote Peer List is NOT Automatically Synchronized

Each node in an EntglDB.Net cluster maintains its own local SQLite database with remote peer configurations. **Remote peer configurations are NOT automatically synchronized across cluster nodes.**

### Why This Matters for Leader Election

When using the `BullyLeaderElectionService`:
1. Any LAN node can be elected as the Cloud Gateway (leader)
2. Only the elected leader connects to remote cloud nodes
3. **If the elected leader doesn't have remote peers configured locally, it won't connect to any cloud nodes**

### Required: Manual Configuration Consistency

**All nodes in a LAN cluster MUST be configured with the same remote peer list** to ensure effective leader election and cloud connectivity.

## 📋 Deployment Patterns

### Pattern 1: Configuration File (Recommended)

Use a shared configuration file deployed to all nodes:

**appsettings.json** (deployed to all LAN nodes):
```json
{
  "EntglDb": {
    "Node": {
      "NodeId": "node-1",  // Unique per node
      "TcpPort": 9000
    },
    "RemotePeers": [
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

**Startup code** (apply on each node):
```csharp
var config = configuration.GetSection("EntglDb:RemotePeers")
    .Get<List<RemotePeerConfig>>();

var peerManagement = new PeerManagementService(peerStore, logger);

foreach (var remotePeer in config)
{
    await peerManagement.AddCloudPeerAsync(
        remotePeer.NodeId,
        remotePeer.Address,
        remotePeer.OAuth2
    );
}
```

### Pattern 2: Centralized Configuration Management

Use a configuration management tool (Ansible, Puppet, etc.) to ensure consistency:

```yaml
# ansible-playbook.yml
- name: Configure EntglDB remote peers
  hosts: entgldb_cluster
  tasks:
    - name: Add cloud peer
      shell: |
        dotnet run -- add-cloud-peer \
          --node-id cloud-node-1 \
          --address remote.entgldb.com:9000 \
          --authority https://identity.example.com \
          --client-id lan-cluster-client \
          --client-secret "{{ vault_secret }}"
```

### Pattern 3: Initialization Script

Create an initialization script run on cluster setup:

```bash
#!/bin/bash
# init-cluster.sh - Run on all cluster nodes

# Add cloud peer to local database
curl -X POST http://localhost:8080/api/peers/cloud \
  -H "Content-Type: application/json" \
  -d '{
    "nodeId": "cloud-node-1",
    "address": "remote.entgldb.com:9000",
    "oauth2": {
      "authority": "https://identity.example.com",
      "clientId": "lan-cluster-client",
      "clientSecret": "secret"
    }
  }'
```

## 🔄 Synchronization Strategies (Future Enhancement)

### Option A: Gossip Protocol for Configuration

Add a gossip-based synchronization for remote peer configurations:

```csharp
// Future enhancement - NOT currently implemented
public class RemotePeerConfigSyncService
{
    // Gossip remote peer configs during normal sync
    // Each node broadcasts its remote peer list
    // Nodes merge and persist new remote peer configurations
}
```

### Option B: Leader-Based Distribution

Leader distributes its remote peer list to all members:

```csharp
// Future enhancement - NOT currently implemented
public class LeaderConfigDistributionService
{
    // When elected as leader, broadcast remote peer configs
    // Members update their local database to match leader
}
```

### Option C: External Configuration Service

Use a centralized configuration service (Consul, etcd):

```csharp
// Future enhancement - NOT currently implemented
public class ConsulRemotePeerProvider : IPeerConfigProvider
{
    // Fetch remote peer configs from Consul
    // All nodes read from same Consul key
}
```

## ✅ Verification

After deploying remote peer configurations, verify consistency:

```csharp
// Query remote peers on each node
var peers = await peerManagement.GetAllRemotePeersAsync();

foreach (var peer in peers)
{
    Console.WriteLine($"{peer.NodeId}: {peer.Address} (Enabled: {peer.IsEnabled})");
}
```

All nodes should return identical lists.

## 🚨 Common Issues

### Issue: Leader has no cloud peers configured

**Symptom**: Leader is elected but no cloud synchronization occurs

**Cause**: Leader node's local database doesn't have remote peer configurations

**Solution**: Ensure remote peer configurations are deployed to ALL nodes, including the one that became leader

### Issue: Inconsistent configurations across nodes

**Symptom**: Some nodes have cloud peers, others don't; intermittent cloud connectivity

**Cause**: Manual configuration only applied to subset of nodes

**Solution**: Use deployment patterns above to ensure consistency

## 📝 Checklist for Production Deployment

- [ ] All nodes deployed with same remote peer configuration
- [ ] OAuth2 credentials secured (environment variables, secrets manager)
- [ ] Remote peer configurations verified on all nodes
- [ ] Leader election tested with failover scenarios
- [ ] Monitoring alerts for configuration drift

## 🔮 Future Improvements

The following enhancements are planned for future releases:

1. **Automatic Configuration Sync**: Remote peer configs synchronized via gossip protocol
2. **Configuration Version Tracking**: Detect and alert on configuration drift
3. **Centralized Configuration API**: Single endpoint to configure entire cluster
4. **Configuration Validation**: Pre-deployment checks for consistency

---

**Current Status**: Manual configuration consistency required  
**Recommended**: Use Pattern 1 (Configuration File) for most deployments  
**Future**: Automatic synchronization (planned for v0.9.0)
