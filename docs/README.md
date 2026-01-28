# EntglDb Documentation

This folder contains the official documentation for EntglDb, published as GitHub Pages.

## Documentation Structure (v0.9.0)

### Getting Started
- **[Getting Started](getting-started.md)** - Installation, setup, and first steps with EntglDb

### Core Documentation
- **[Architecture](architecture.md)** - Hybrid Logical Clocks, Gossip Protocol, mesh networking
- **[API Reference](api-reference.md)** - Complete API documentation with examples
- **[Querying](querying.md)** - Data querying patterns and LINQ support

### Persistence & Storage
- **[Persistence Providers](persistence-providers.md)** - SQLite, EF Core, PostgreSQL comparison
- **[Deployment Modes](deployment-modes.md)** - Single vs Multi-cluster strategies

### Networking & Security
- **[Security](security.md)** - Encryption, authentication, secure networking
- **[Conflict Resolution](conflict-resolution.md)** - LWW and Recursive Merge strategies
- **[Network Telemetry](network-telemetry.md)** - Monitoring and diagnostics
- **[Dynamic Reconfiguration](dynamic-reconfiguration.md)** - Runtime configuration changes
- **[Remote Peer Configuration](remote-peer-configuration.md)** - Managing remote peers

### Deployment & Operations
- **[Deployment (LAN)](deployment-lan.md)** - Platform-specific deployment guide
- **[Production Hardening](production-hardening.md)** - Configuration, monitoring, best practices

## Building the Documentation

This documentation uses Jekyll with the Cayman theme and is automatically published via GitHub Pages.

### Local Development

```bash
# Install Jekyll (requires Ruby)
gem install bundler jekyll

# Serve documentation locally
cd docs
jekyll serve

# Open http://localhost:4000
```

### Site Configuration

- **_config.yml** - Jekyll configuration and site metadata
- **_layouts/default.html** - Main page layout with navigation and styling
- **_includes/nav.html** - Top navigation bar
- **_data/navigation.yml** - Sidebar navigation structure per version

## Version History

The documentation supports multiple versions:
- **v0.9** (current) - Latest stable release
- **v0.8** - ASP.NET Core hosting, EF Core, PostgreSQL support
- **v0.7** - Brotli compression, Protocol v4
- **v0.6** - Secure networking, conflict resolution strategies

## Contributing to Documentation

1. **Fix typos or improve clarity** - Submit PRs directly
2. **Add new guides** - Create a new .md file and update navigation.yml
3. **Test locally** - Run Jekyll locally to preview changes before submitting

## Links

- [Main Repository](https://github.com/EntglDb/EntglDb.Net)
- [NuGet Packages](https://www.nuget.org/packages?q=EntglDb)
