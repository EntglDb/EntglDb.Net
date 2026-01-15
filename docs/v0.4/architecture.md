# Architecture & Concepts

## Design Philosophy

EntglDb is designed for **Local Area Networks (LAN)** and **Local-First** scenarios.  
It does not rely on a central master server. Every node is equal (Peer-to-Peer).

**Target Deployment**: Trusted LAN environments (offices, homes, private networks)  
**Cross-Platform**: Windows, Linux, macOS (.NET 10+)

### HLC (Hybrid Logical Clock)
To resolve conflicts without a central authority, we use **Hybrid Logical Clocks**.
This allows us to determine the "happened-before" relationship between events even if system clocks are slightly skewed.
In case of concurrent edits, the "Last Write Wins" (LWW) policy based on HLC is applied.

## Synchronization

### Anti-Entropy
When two nodes connect, they exchange their latest HLC timestamps.
- If Node A is ahead of Node B, Node B "pulls" the missing operations from Node A.
- If Node B is ahead, Node A "pushes" its operations.

### Gossip Protocol
Nodes discover each other via UDP Broadcast (LAN) and then form random TCP connections to gossip updates.
This ensures that updates propagate exponentially through the network (Epidemic Algorithm).

## Security Disclaimer

::: warning FOR LAN USE ONLY
**EntglDb is designed for trusted Local Area Networks.**
:::

- **Target Environment**: LAN/private networks (offices, homes, edge deployments)
- **NOT for Public Internet**: Requires additional security measures for internet deployment
- **Transport**: Data is transmitted via raw TCP. There is **NO Encryption (TLS/SSL)** by default.
- **Authentication**: A basic "Shared Key" mechanism is implemented. Nodes must share the same `AuthToken` to sync.
- **Authorization**: Once authenticated, a node has full read/write access to all collections.

**Recommendation**: 
- Use only within trusted private networks (LAN, VPN, or localhost)
- For internet deployment, implement TLS, proper authentication, and firewall rules
- Consider the production hardening features for resilience on LAN

**Cross-Platform Support**: Runs on Windows, Linux, and macOS with .NET 10+.
