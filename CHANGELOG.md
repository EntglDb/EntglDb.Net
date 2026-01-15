# Changelog

All notable changes to EntglDb will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned
- Merkle Trees for efficient sync
- TLS/SSL support for secure networks
- Query optimization & indexing improvements
- Compressed sync protocol
- Admin UI / monitoring dashboard

---

<a name="0.3.1"></a>
## [0.3.1] - 2026-01-15

### Added
- **NuGet Package Metadata**: Complete metadata for all packages
  - Package-specific README files for Core, Network, and Persistence.Sqlite
  - Package icon (blue-purple mesh network design)
  - Repository and project URLs
  - Enhanced package tags for better discoverability
- **Assets**: Professional icon for NuGet packages

### Improved
- Better NuGet package presentation with README visible on NuGet.org
- More comprehensive package tags for search optimization

---

<a name="0.3.0"></a>
## [0.3.0] - 2026-01-15

### Changed
- **Stable Release**: First stable release, promoted from 0.2.0-alpha
- All production hardening features now stable and ready for LAN deployment

### Added
- GitHub Actions workflow for automated NuGet publishing
- CHANGELOG.md for version tracking

---

<a name="0.2.0-alpha"></a>
## [0.2.0-alpha] - 2026-01-15

### Added - Production Hardening for LAN
- **Configuration System**: EntglDbOptions with appsettings.json support for flexible configuration
- **Exception Hierarchy**: 6 custom exceptions with error codes (NetworkException, PersistenceException, SyncException, ConfigurationException, DatabaseCorruptionException, TimeoutException)
- **RetryPolicy**: Exponential backoff with configurable attempts and transient error detection
- **DocumentCache**: LRU cache with statistics (hits, misses, hit rate) for improved performance
- **OfflineQueue**: Resilient offline operations queue with configurable size limits
- **SyncStatusTracker**: Comprehensive sync monitoring with peer tracking and error history
- **EntglDbHealthCheck**: Database and network health monitoring
- **SQLite Resilience**: WAL mode verification, integrity checking, and backup functionality

### Enhanced
- **SqlitePeerStore**: Added logging integration, WAL mode enforcement, integrity checks, and backup methods
- **Sample Application**: Updated with production features demo, appsettings.json configuration, and interactive commands (health, cache, backup)

### Documentation
- **Production Hardening Guide**: Complete implementation guide with examples and best practices
- **LAN Deployment Guide**: Platform-specific deployment instructions for Windows, Linux, and macOS
- **README**: Comprehensive update with table of contents, architecture diagrams, use cases, and contributing guidelines
- **LAN Disclaimers**: Clear positioning as LAN-focused, cross-platform database throughout all documentation

### Fixed
- Sample application startup crash due to directory creation issue

### Changed
- All projects updated to version 0.2.0-alpha
- Enhanced logging throughout the codebase
- Improved error handling with structured exceptions

---

<a name="0.1.0-alpha"></a>
## [0.1.0-alpha] - 2026-01-13

### Added - Initial Release
- **Core P2P Database**: Lightweight peer-to-peer database for .NET
- **Mesh Networking**: Automatic peer discovery via UDP broadcast
- **TCP Synchronization**: Reliable data sync between nodes
- **Hybrid Logical Clocks (HLC)**: Distributed timestamp-based conflict resolution
- **Last-Write-Wins (LWW)**: Automatic conflict resolution strategy
- **Type-Safe API**: Generic Collection<T> with LINQ support
- **SQLite Persistence**: Local database storage with Dapper
- **Auto-Generated Keys**: Support for [PrimaryKey(AutoGenerate = true)] attribute
- **Indexed Properties**: Support for [Indexed] attribute for query optimization
- **Expression-based Queries**: LINQ support for filtering (e.g., `Find(u => u.Age > 30)`)
- **Gossip Protocol**: Efficient update propagation across the network
- **Anti-Entropy Sync**: Automatic reconciliation between peers
- **Offline-First**: Full local database, works without network connection
- **Cross-Platform**: Runs on Windows, Linux, and macOS (.NET 10)

### Documentation
- Initial README with quick start guide
- Architecture documentation with HLC and Gossip explanations
- API reference documentation
- Sample console application

### Tests
- Unit tests for Core (19 tests)
- Unit tests for Persistence.Sqlite (13 tests)
- Unit tests for Network (1 test)

---

[Unreleased]: https://github.com/lucafabbri/EntglDb/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/lucafabbri/EntglDb/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/lucafabbri/EntglDb/compare/v0.2.0-alpha...v0.3.0
[0.2.0-alpha]: https://github.com/lucafabbri/EntglDb/compare/v0.1.0-alpha...v0.2.0-alpha
[0.1.0-alpha]: https://github.com/lucafabbri/EntglDb/releases/tag/v0.1.0-alpha
