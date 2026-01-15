# EntglDb.Persistence.Sqlite

SQLite persistence provider for **EntglDb** - provides reliable local storage with production-ready features.

## What's Included

This package provides SQLite persistence for EntglDb:

- **SQLite Storage**: Fast, reliable local database
- **WAL Mode**: Better concurrency and crash recovery
- **Production Features**:
  - Integrity checking
  - Automated backups
  - Corruption detection
  - Logging integration

## Installation

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

## Quick Start

```csharp
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;

var logger = LoggerFactory.Create(b => b.AddConsole())
    .CreateLogger<SqlitePeerStore>();

var store = new SqlitePeerStore(
    "Data Source=myapp.db",
    logger
);

// The store handles all persistence
// - Automatic WAL mode
// - Table creation
// - Indexing
// - Query optimization
```

## Features

### Production-Ready
- **WAL Mode**: Automatic Write-Ahead Logging for better concurrency
- **Integrity Checks**: Verify database health
- **Backups**: Create database snapshots
- **Corruption Detection**: Automatic detection and handling

### Performance
- **Indexed Queries**: Automatic indexing for `[Indexed]` properties
- **Batch Operations**: Efficient bulk inserts/updates
- **Connection Pooling**: Optimized SQLite connection usage

## Example

```csharp
// Health check
var isHealthy = await store.CheckIntegrityAsync();

// Create backup
await store.BackupAsync("backups/backup-20260115.db");
```

## Documentation

- **[Production Hardening](https://github.com/lucafabbri/EntglDb/blob/main/docs/production-hardening.md)**
- **[SQLite Configuration](https://github.com/lucafabbri/EntglDb/blob/main/docs/deployment-lan.md)**

## Related Packages

- **EntglDb.Core** - Core database abstractions
- **EntglDb.Network** - P2P networking layer

## License

MIT - see [LICENSE](https://github.com/lucafabbri/EntglDb/blob/main/LICENSE)
