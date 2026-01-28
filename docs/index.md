---
layout: default
---

# EntglDb Documentation

Welcome to the EntglDb documentation for **version 0.9.0**.

## What's New in v0.9.0

Version 0.9.0 brings enhanced ASP.NET Core support, improved EF Core runtime stability, and refined synchronization and persistence layers.

- **ASP.NET Core Enhancements**: Improved sample application with better error handling
- **EF Core Stability**: Fixed runtime issues and improved persistence layer reliability
- **Sync & Persistence**: Enhanced stability across all persistence providers
- **Production Ready**: All features tested and stable for production use

See the [CHANGELOG](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md) for complete details.

## Documentation

### Getting Started
*   [**Getting Started**](getting-started.html) - Installation, basic setup, and your first EntglDb application

### Core Concepts
*   [Architecture](architecture.html) - Understanding HLC, Gossip Protocol, and P2P mesh networking
*   [API Reference](api-reference.html) - Complete API documentation and examples
*   [Querying](querying.html) - Data querying patterns and LINQ support

### Persistence & Storage
*   [Persistence Providers](persistence-providers.html) - SQLite, EF Core, PostgreSQL comparison and configuration
*   [Deployment Modes](deployment-modes.html) - Single vs Multi-cluster deployment strategies

### Networking & Security
*   [Security](security.html) - Encryption, authentication, and secure networking
*   [Conflict Resolution](conflict-resolution.html) - LWW and Recursive Merge strategies
*   [Network Telemetry](network-telemetry.html) - Monitoring and diagnostics
*   [Dynamic Reconfiguration](dynamic-reconfiguration.html) - Runtime configuration and leader election
*   [Remote Peer Configuration](remote-peer-configuration.html) - Managing remote peers

### Deployment & Operations
*   [Deployment (LAN)](deployment-lan.html) - Platform-specific deployment instructions
*   [Production Hardening](production-hardening.html) - Configuration, monitoring, and best practices

## Previous Versions

- [v0.8.x Documentation](v0.8/getting-started.html)
- [v0.7.x Documentation](v0.7/getting-started.html)
- [v0.6.x Documentation](v0.6/getting-started.html)

## Downloads

*   [**Download EntglStudio**](https://github.com/EntglDb/EntglDb.Net/releases) - Standalone tool for managing EntglDb data.

## Links

*   [GitHub Repository](https://github.com/EntglDb/EntglDb.Net)
*   [NuGet Packages](https://www.nuget.org/packages?q=EntglDb)
*   [Changelog](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md)
