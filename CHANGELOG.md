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

<a name="0.9.0"></a>
## [0.9.0](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v0.9.0) (2026-01-28)

### Features

* enhance ASP.NET Core sample, fix EF Core runtime issues, stabilize Sync/Persistence ([18813d0](https://www.github.com/EntglDb/EntglDb.Net/commit/18813d0aa881de9d0a13bd8d863ab6283bc630fc))

<a name="0.8.6"></a>
## [0.8.6](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v0.8.6) (2026-01-27)

### Features

* **persistence:** snapshots ([05068ff](https://www.github.com/EntglDb/EntglDb.Net/commit/05068ff70d8cafcf2ca292a80ddb3c14039ce30a))

### Bug Fixes

* prevent snapshot infinite loop via boundary convergence check ([e10b80a](https://www.github.com/EntglDb/EntglDb.Net/commit/e10b80a424b3fa521fc8f716eeecc329ba72648d))
* query limits ([d74e689](https://www.github.com/EntglDb/EntglDb.Net/commit/d74e6892a027eac367feadba2ab96f7758af320b))

<a name="0.8.5"></a>
## [0.8.5](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v0.8.5) (2026-01-26)

### Bug Fixes

* **core:** atomic save and last hash update ([e9cf4b3](https://www.github.com/EntglDb/EntglDb.Net/commit/e9cf4b3f3738a725f7dbcbd79bb4c0f393203cbd))
* **core:** makes hashing deterministric ([9400c73](https://www.github.com/EntglDb/EntglDb.Net/commit/9400c7334d6debf896a3c234b085a2e9f4222023))

<a name="0.8.0"></a>
## [0.8.0] - 2026-01-20

### Added - Phase 2: ASP.NET Server + Persistence Layers

#### EntglDb.Persistence.EntityFramework Package
- **Generic EF Core Persistence**: Full IPeerStore implementation using Entity Framework Core
- **Multi-Database Support**: SQL Server, PostgreSQL, MySQL, and SQLite support
- **Entity Classes**: DocumentEntity, OplogEntity, RemotePeerEntity with proper indexes
- **EntglDbContext**: Configured DbContext with optimized model configuration
- **DI Extensions**: AddEntglDbEntityFramework() methods for easy service registration
- **Migration Support**: Initial EF Core migrations for all supported databases

#### EntglDb.Persistence.PostgreSQL Package
- **JSONB Optimization**: PostgreSQL-specific DbContext with JSONB column types
- **GIN Indexes**: Configured for high-performance JSON queries
- **PostgreSqlPeerStore**: Optimized persistence layer extending EfCorePeerStore
- **Connection Resilience**: Built-in retry logic and connection pooling
- **Production Ready**: Optimized for PostgreSQL 12+ with best practices

#### EntglDb.AspNet Package
- **Dual Deployment Modes**: Single Cluster (production) and Multi Cluster (dev/staging)
- **Configuration Models**: ServerMode, SingleClusterOptions, MultiClusterOptions
- **NoOpDiscoveryService**: Passive discovery for server scenarios (no UDP broadcast)
- **NoOpSyncOrchestrator**: Respond-only sync mode for cloud deployments
- **Health Checks**: EntglDbHealthCheck for monitoring and observability
- **Hosted Services**: TcpSyncServerHostedService and DiscoveryServiceHostedService
- **OAuth2 Integration**: JWT token validation for cloud authentication
- **DI Extensions**: AddEntglDbAspNetSingleCluster() and AddEntglDbAspNetMultiCluster()

### Documentation
- **docs/deployment-modes.md**: Comprehensive guide to Single vs Multi-cluster deployment
- **docs/persistence-providers.md**: Detailed comparison of SQLite, EF Core, and PostgreSQL options
- **Package READMEs**: Complete documentation for each new package with examples
- **Configuration Examples**: JSON configuration samples for both deployment modes

### Enhanced
- **Production Infrastructure**: Complete cloud deployment stack built on Phase 1 foundations
- **Database Flexibility**: Choose the right database for your deployment scenario
- **Hosting Options**: Deploy as standalone ASP.NET Core application or container
- **Security**: OAuth2 JWT validation integrated throughout the stack

### Compatibility
- **Zero Breaking Changes**: Full backward compatibility with existing v0.7.x code
- **All Tests Pass**: 50/50 tests passing (27 Core + 8 Network + 15 SQLite)
- **Incremental Adoption**: Can use new packages alongside existing SQLite persistence

---

<a name="0.6.1"></a>
## [0.6.1] - 2026-01-18

### Fixed
- **Serialization**: Standardized JSON serialization to use `snake_case` naming policy for `node_id` and `tcp_port` in `DiscoveryBeacon` to match other platforms.
- **Discovery**: Improved interoperability with Android nodes by ensuring consistent payload format.

<a name="0.6.0"></a>
## [0.6.0] - 2026-01-16

### Added
- **Batch Operations**: `PutMany` and `DeleteMany` for efficient bulk processing
- **Filtered Count**: `Count(predicate)` support leveraging database-side counting
- **Global Configuration**: `EntglDbMapper` for code-based entity and index configuration
- **Typed Exceptions**: `DocumentNotFoundException` and `EntglDbConcurrencyException` for robust error handling
- **Delta Sync**: `FetchUpdatedSince` support using HLC Oplog for efficient incremental updates

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

[Unreleased]: https://github.com/EntglDb/EntglDb.Net/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/EntglDb/EntglDb.Net/compare/v0.6.1...v0.8.0
[0.6.1]: https://github.com/EntglDb/EntglDb.Net/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/EntglDb/EntglDb.Net/compare/v0.3.1...v0.6.0
[0.3.1]: https://github.com/EntglDb/EntglDb.Net/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/EntglDb/EntglDb.Net/compare/v0.2.0-alpha...v0.3.0
[0.2.0-alpha]: https://github.com/EntglDb/EntglDb.Net/compare/v0.1.0-alpha...v0.2.0-alpha
[0.1.0-alpha]: https://github.com/EntglDb/EntglDb.Net/releases/tag/v0.1.0-alpha
