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

<a name="2.1.0"></a>
## [2.1.0](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v2.1.0) (2026-03-19)

### Features

* ContentHash on DocumentMetadata with canonical JSON normalization ([dfa856f](https://www.github.com/EntglDb/EntglDb.Net/commit/dfa856f766907e6ca40de63146eaed0533cc1a39))

<a name="2.0.1"></a>
## [2.0.1](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v2.0.1) (2026-03-18)

### Bug Fixes

* P2P sync correctness - CDC serialization, LWW timestamps, deterministic metadata IDs ([53514bd](https://www.github.com/EntglDb/EntglDb.Net/commit/53514bd3cfd5b7ea619296b2adfb49d19c91b672))

<a name="2.0.0"></a>
## [2.0.0](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v2.0.0) (2026-03-17)

### Features

* Add dynamic database paths and per-collection tables to SQLite persistence ([57b8906](https://www.github.com/EntglDb/EntglDb.Net/commit/57b8906444bf7ecc0ea1e3727d5d4d93f7ddd6ba))
* Centralize EntglDbNodeService in Network package with auto-registration ([9b7c6be](https://www.github.com/EntglDb/EntglDb.Net/commit/9b7c6bed20f4463f871b08abfd9512a88a61ba85))
* enhance ASP.NET Core sample, fix EF Core runtime issues, stabilize Sync/Persistence ([18813d0](https://www.github.com/EntglDb/EntglDb.Net/commit/18813d0aa881de9d0a13bd8d863ab6283bc630fc))
* full async read and writes for BLite ([e49d44a](https://www.github.com/EntglDb/EntglDb.Net/commit/e49d44a3390e4029ab878aff36b8d788157b6bf9))
* Implement automatic synchronization of remote peer configurations ([4e7d064](https://www.github.com/EntglDb/EntglDb.Net/commit/4e7d06417c1ad031adfd8358695045a066c9e005))
* Phase 1 - Core infrastructure for remote cloud nodes ([2f2d916](https://www.github.com/EntglDb/EntglDb.Net/commit/2f2d9164290733504961528ddfa900dc4d194c79))
* Phase 2 - Discovery and Leader Election ([68c7866](https://www.github.com/EntglDb/EntglDb.Net/commit/68c7866244a5d3772c47bf18cb7010a06b299fd6))
* Phase 3 - OAuth2 Security Infrastructure ([8c6a8e2](https://www.github.com/EntglDb/EntglDb.Net/commit/8c6a8e2b49a31b562f0b41e594fa90713fa9e088))
* Phase 5 - Management API for Remote Peers ([8e66710](https://www.github.com/EntglDb/EntglDb.Net/commit/8e667101fdabda784881cebdc70aeb27a94de877))
* vector sync and hash-chain OplogEntry ([240ca26](https://www.github.com/EntglDb/EntglDb.Net/commit/240ca2695d7d2701926e294db2841fc993c7c318))
* **persistence:** snapshots ([05068ff](https://www.github.com/EntglDb/EntglDb.Net/commit/05068ff70d8cafcf2ca292a80ddb3c14039ce30a))

### Bug Fixes

* adds cancellation token to async blite mutations ([4b87114](https://www.github.com/EntglDb/EntglDb.Net/commit/4b8711458674ebc1783f522996e1a7872c271ec7))
* adds cancellation token to async blite mutations ([914a705](https://www.github.com/EntglDb/EntglDb.Net/commit/914a705a429bba56c9f11e2db8cf623ddab641d6))
* adds support to Set in BLite context ([5521e29](https://www.github.com/EntglDb/EntglDb.Net/commit/5521e298df6654850548ad8b8e76e24f9cb57971))
* corrects hash-chain consistency ([84388d8](https://www.github.com/EntglDb/EntglDb.Net/commit/84388d806430415fcae6372c110997c141375491))
* corrects issue with samples ([58d368e](https://www.github.com/EntglDb/EntglDb.Net/commit/58d368e367c3cfa1ba13f78845c3361189cc1100))
* corrects samples ([774abe5](https://www.github.com/EntglDb/EntglDb.Net/commit/774abe5623dc324192085926966145d64f887bf1))
* corrects the samples ([cc8dc82](https://www.github.com/EntglDb/EntglDb.Net/commit/cc8dc82d719baae8b47eb8671f3460f9d8fdcea2))
* prevent snapshot infinite loop via boundary convergence check ([e10b80a](https://www.github.com/EntglDb/EntglDb.Net/commit/e10b80a424b3fa521fc8f716eeecc329ba72648d))
* query limits ([d74e689](https://www.github.com/EntglDb/EntglDb.Net/commit/d74e6892a027eac367feadba2ab96f7758af320b))
* Use IEntglDbNode interface in EntglDbNodeService for proper dependency inversion ([cd7e552](https://www.github.com/EntglDb/EntglDb.Net/commit/cd7e552716b18d5e9e968daee453a78a296df354))
* watch only local collections ([ef4c345](https://www.github.com/EntglDb/EntglDb.Net/commit/ef4c345cc4b0bf4fe45e7297c925e5e4e68ee1fe))
* **core:** atomic save and last hash update ([e9cf4b3](https://www.github.com/EntglDb/EntglDb.Net/commit/e9cf4b3f3738a725f7dbcbd79bb4c0f393203cbd))
* **core:** makes hashing deterministric ([9400c73](https://www.github.com/EntglDb/EntglDb.Net/commit/9400c7334d6debf896a3c234b085a2e9f4222023))
* **network:** corrects protocol ([464344c](https://www.github.com/EntglDb/EntglDb.Net/commit/464344c647ec5fd69073967bc34c5c6f0102eb1a))
* **network:** imrproves socket read/write for android ([1dde9a6](https://www.github.com/EntglDb/EntglDb.Net/commit/1dde9a6dd60dbdec8ab758fa15b9c2fab8c871a4))
* **network:** resolve critical concurrency and connection issues ([eb243a5](https://www.github.com/EntglDb/EntglDb.Net/commit/eb243a5ee06a00205b69d620b9ea3456fde0f401))
* **sqlite:** handle Undefined values in OpLogEntry serialization ([b7b2388](https://www.github.com/EntglDb/EntglDb.Net/commit/b7b238846547f05ee634668f1865e599cfa72083))
* **sqlite:** handle Undefined values in OpLogEntry serialization ([c3173eb](https://www.github.com/EntglDb/EntglDb.Net/commit/c3173eb4c6c86023449ebace5079c4c70f4256ce))

### Breaking Changes

* Develop (#14) ([adb6245](https://www.github.com/EntglDb/EntglDb.Net/commit/adb6245a055feb858f9b0d82cb4d55cee2221f29))
* bump to v2.0.0 — target netstandard2.1;net10.0 ([8a3b727](https://www.github.com/EntglDb/EntglDb.Net/commit/8a3b7279b487d44769794e45f91bb89a42be65b0))

<a name="2.0.0"></a>
## [2.0.0] (2026-03-17)

### Breaking Changes

* **Target frameworks**: rimossi `netstandard2.0`, `net6.0`, `net8.0`; le librerie ora puntano a `netstandard2.1;net10.0` (eccetto `EntglDb.AspNet` e `EntglDb.Persistence.EntityFramework` che puntano solo a `net10.0`)
* Rimossa dipendenza da `Microsoft.AspNetCore.Http.Abstractions` in `EntglDb.AspNet` (fornita dal framework in net10.0)

### Changed

* Tutti i pacchetti `Microsoft.Extensions.*` aggiornati alla 10.0.5
* `Microsoft.EntityFrameworkCore` e `Microsoft.EntityFrameworkCore.Relational` aggiornati alla 10.0.5
* `System.Text.Json` e `System.Threading.Channels` referenziati come pacchetti solo per `netstandard2.1` (built-in per `net10.0`)
* Progetti di test aggiornati a `net10.0`; versioni pacchetti di test aggiornate (coverlet.collector 8.0.0, FluentAssertions 8.9.0, Microsoft.NET.Test.Sdk 18.3.0, xunit.runner.visualstudio 3.1.5)

---

<a name="1.1.7"></a>
## [1.1.7](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.7) (2026-03-12)

<a name="1.1.6"></a>
## [1.1.6](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.6) (2026-03-11)

### Bug Fixes

* watch only local collections ([ef4c345](https://www.github.com/EntglDb/EntglDb.Net/commit/ef4c345cc4b0bf4fe45e7297c925e5e4e68ee1fe))

<a name="1.1.5"></a>
## [1.1.5](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.5) (2026-03-11)

<a name="1.1.4"></a>
## [1.1.4](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.4) (2026-03-10)

### Bug Fixes

* adds cancellation token to async blite mutations ([914a705](https://www.github.com/EntglDb/EntglDb.Net/commit/914a705a429bba56c9f11e2db8cf623ddab641d6))

<a name="1.1.3"></a>
## [1.1.3](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.3) (2026-03-06)

### Bug Fixes

* adds cancellation token to async blite mutations ([4b87114](https://www.github.com/EntglDb/EntglDb.Net/commit/4b8711458674ebc1783f522996e1a7872c271ec7))

<a name="1.1.2"></a>
## [1.1.2](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.2) (2026-03-06)

<a name="1.1.1"></a>
## [1.1.1](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.1) (2026-03-02)

### Bug Fixes

* corrects the samples ([cc8dc82](https://www.github.com/EntglDb/EntglDb.Net/commit/cc8dc82d719baae8b47eb8671f3460f9d8fdcea2))

<a name="1.1.0"></a>
## [1.1.0](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.1.0) (2026-02-22)

### Features

* full async read and writes for BLite ([e49d44a](https://www.github.com/EntglDb/EntglDb.Net/commit/e49d44a3390e4029ab878aff36b8d788157b6bf9))

<a name="1.0.3"></a>
## [1.0.3](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.0.3) (2026-02-19)

<a name="1.0.2"></a>
## [1.0.2](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.0.2) (2026-02-19)

<a name="1.0.1"></a>
## [1.0.1](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.0.1) (2026-02-18)

### Bug Fixes

* adds support to Set in BLite context ([5521e29](https://www.github.com/EntglDb/EntglDb.Net/commit/5521e298df6654850548ad8b8e76e24f9cb57971))

<a name="1.0.0"></a>
## [1.0.0](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v1.0.0) (2026-02-18)

### Features

* Add BLiteDocumentStore abstract base class ([7dd5777](https://www.github.com/EntglDb/EntglDb.Net/commit/7dd577740fa041c8e488fa11ab2d8615f7e32e69))
* Add DocumentMetadataStore for HLC timestamp tracking ([26f92d9](https://www.github.com/EntglDb/EntglDb.Net/commit/26f92d9e31d086f69cc07b8abe15f2dffdfc0102))
* Add EfCoreDocumentStore abstract base class ([0805cd9](https://www.github.com/EntglDb/EntglDb.Net/commit/0805cd9566710ce5b562261ea1632b0b9b0da98e))
* Complete BLite persistence with snapshot support ([09bb7c4](https://www.github.com/EntglDb/EntglDb.Net/commit/09bb7c4b470e9654bf7589daf0ea7921f252a032))

### Bug Fixes

* EfCoreOplogStore now initializes NodeCache correctly ([d5da920](https://www.github.com/EntglDb/EntglDb.Net/commit/d5da920a4a1f7ec6add08012309d66d8b271ae00))
* OplogStore now uses SnapshotMetadata for VectorClock initialization ([ae1b2ac](https://www.github.com/EntglDb/EntglDb.Net/commit/ae1b2accd26415454b6c1025257b689a2d8e5d96))
* Remove circular dependency from BLiteDocumentStore ([d344889](https://www.github.com/EntglDb/EntglDb.Net/commit/d344889b96b9956040892589abaa739401875bb5))
* TcpSyncServer returns entries for all nodes instead of requested node ([da869a0](https://www.github.com/EntglDb/EntglDb.Net/commit/da869a0a173045316fbf6f215358b626edb793e0))
* Transaction crash in ApplyBatchAsync for both BLite and EfCore ([af48383](https://www.github.com/EntglDb/EntglDb.Net/commit/af48383268ddb862c1877008f33e720b7333bc8a))
* Vector Clock always empty - OplogStore cache not updated by CDC ([1136604](https://www.github.com/EntglDb/EntglDb.Net/commit/1136604e06f0389f1527c649b2490b71712c3213))
* VectorClock stays empty after Invalidate - _cacheInitialized never reset ([2cccb5d](https://www.github.com/EntglDb/EntglDb.Net/commit/2cccb5d60a4a34c117ebc7b7a5dbc8d4c1354f16))

### Breaking Changes

* update positioning from P2P database to sync middleware ([a396b60](https://www.github.com/EntglDb/EntglDb.Net/commit/a396b60a1020876c92e7d75cb7b6e5e14e43ae69))

<a name="0.9.1"></a>
## [0.9.1](https://www.github.com/EntglDb/EntglDb.Net/releases/tag/v0.9.1) (2026-01-28)

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
