# EntglDb - Deployment Guide for LAN

## Target Environment

EntglDb is specifically designed for **Local Area Networks (LAN)** in trusted environments:

✅ **Ideal Use Cases**:
- Office networks (employee workstations, kiosks)
- Home automation systems
- Retail point-of-sale systems (POS)
- Edge computing deployments
- Private industrial networks
- Development/testing environments

❌ **NOT Recommended**:
- Public internet deployment (without significant security enhancements)
- Multi-tenant SaaS applications
- Untrusted network environments

## Cross-Platform Support

EntglDb runs on all major operating systems:

| Platform | Support | Notes |
|----------|---------|-------|
| **Windows** | ✅ Full | Windows 10+, Server 2019+ |
| **Linux** | ✅ Full | Ubuntu, Debian, RHEL, Alpine |
| **macOS** | ✅ Full | macOS 11+ (Big Sur and later) |

**Requirements**: .NET 8.0+ Runtime (also compatible with .NET 6.0 and .NET Standard 2.0)

## LAN Deployment Checklist

### Network Configuration

- [ ] **Firewall Rules**: Open TCP port (default: 5000) and UDP port (default: 6000)
- [ ] **Broadcast Domain**: Ensure nodes are in the same subnet for UDP discovery
- [ ] **Network Stability**: LAN should have reasonable stability (WiFi or wired)
- [ ] **Bandwidth**: Adequate for sync operations (typically low, < 1 Mbps)

### Security Configuration

- [ ] **Cluster Key**: Configure unique cluster authentication key
- [ ] **Network Isolation**: Use VLANs or network segmentation
- [ ] **Access Control**: Limit network access to authorized devices
- [ ] **Monitoring**: Set up logging and health checks

### Application Configuration

```json
{
  "EntglDb": {
    "Network": {
      "TcpPort": 5000,
      "UdpPort": 6000,
      "LocalhostOnly": false
    },
    "Persistence": {
      "DatabasePath": "/var/lib/entgldb/data.db",
      "EnableWalMode": true,
      "EnableAutoBackup": true,
      "BackupPath": "/var/lib/entgldb/backups"
    }
  }
}
```

### Platform-Specific Considerations

#### Windows
- Use Windows Services for background operation
- Configure Windows Firewall rules
- Consider SQLite file locking on network shares

#### Linux
- Use systemd for service management
- Set appropriate file permissions
- Consider SELinux/AppArmor policies

#### macOS
- Use launchd for background services
- Configure macOS firewall
- Handle macOS sleep/wake for laptops

## Example: Office Network Deployment

### Scenario
10 workstations in an office need to sync product catalog data.

### Setup
1. **Network**: All on 192.168.1.0/24 subnet
2. **Nodes**: Each workstation runs EntglDb
3. **Discovery**: UDP broadcast for automatic peer discovery
4. **Sync**: TCP for data synchronization
5. **Storage**: Local SQLite database per workstation

### Benefits
- **No Internet Required**: Works during internet outages
- **Low Latency**: Local network = fast reads/writes
- **Resilient**: No single point of failure
- **Offline Capable**: Each workstation works independently

## Troubleshooting

### Nodes Not Discovering Each Other
- Check firewall rules for UDP port
- Verify nodes are on same broadcast domain
- Check cluster key matches on all nodes

### Slow Synchronization
- Check network bandwidth
- Verify no packet loss
- Review batch size configuration

### Database Corruption
- Verify WAL mode is enabled
- Check disk space
- Review backup/restore procedures

## Security Best Practices for LAN

1. **Network Segmentation**: Isolate EntglDb network from public networks
2. **Cluster Authentication**: Use strong cluster keys
3. **Access Control**: Limit which devices can join the network
4. **Monitoring**: Log all sync operations
5. **Regular Backups**: Automated backup to separate storage
6. **Update Policy**: Keep .NET runtime updated

## NOT Recommended for Internet

EntglDb **should NOT** be deployed on public internet without:
- TLS/SSL encryption for TCP connections
- Proper authentication beyond cluster key
- Network firewalls and security groups
- DDoS protection
- Rate limiting
- Intrusion detection

For internet deployment, consider traditional client-server databases instead.

## Support

For LAN deployment questions, see:
- [Production Hardening Guide](production-hardening.md)
- [API Reference](api-reference.md)
- [Architecture Documentation](architecture.md)
