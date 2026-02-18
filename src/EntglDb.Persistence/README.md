# EntglDb.Persistence

Base persistence abstractions and implementations for **EntglDb** - provides foundational storage components used by all persistence providers.

## What's Included

This package provides core persistence services that other providers extend:

- **OplogStore**: Base implementation for append-only operation log storage
- **VectorClockService**: Thread-safe in-memory vector clock management
- **DocumentMetadataStore**: Document versioning and metadata tracking
- **SnapshotStore**: Snapshot creation and restoration logic
- **PeerConfigurationStore**: Peer node configuration persistence

## Architecture Role

EntglDb.Persistence sits between the core abstractions (`EntglDb.Core`) and concrete implementations:

```
EntglDb.Core (interfaces)
       |
       v
EntglDb.Persistence (base implementations)
       |
       v
    +------------------+------------------------+
    |                  |                        |
  BLite         EntityFramework          (Other Providers)
```

## When To Use This Package

- **As a Library User**: You typically don't install this directly - it's included as a dependency when you install a concrete provider like `EntglDb.Persistence.BLite` or `EntglDb.Persistence.EntityFramework`
- **As a Provider Developer**: Reference this package to build custom persistence providers by extending the base classes

## Key Components

### OplogStore
Base implementation for operation log storage with:
- Hash-chain verification
- Batch application
- Conflict resolution integration
- Change event notifications

### VectorClockService
Thread-safe vector clock management:
- In-memory caching for fast lookups
- Atomic updates
- Causal ordering tracking

### SnapshotStore
Snapshot lifecycle management:
- Creation and compression
- Restoration logic
- Metadata tracking

## Creating a Custom Provider

To build your own persistence provider:

```csharp
public class MyCustomOplogStore : OplogStore
{
    public MyCustomOplogStore(
        IDocumentStore documentStore,
        IConflictResolver conflictResolver,
        ISnapshotMetadataStore? snapshotMetadataStore,
        IVectorClockService vectorClock)
        : base(documentStore, conflictResolver, snapshotMetadataStore, vectorClock)
    {
    }

    protected override async Task<List<OplogEntry>> GetNodeEntriesAsync(
        string nodeId, CancellationToken cancellationToken)
    {
        // Implement storage-specific logic
    }

    // Implement other abstract methods...
}
```

## Related Packages

- **EntglDb.Core** - Core abstractions and interfaces
- **EntglDb.Persistence.BLite** - BLite embedded provider
- **EntglDb.Persistence.EntityFramework** - EF Core provider
- **EntglDb.Network** - P2P networking layer

## Documentation

- **[Architecture](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/architecture.md)**
- **[Persistence Providers](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/persistence-providers.md)**
- **[Getting Started](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/getting-started.md)**

## Installation

```bash
# Install a concrete provider (includes this package automatically)
dotnet add package EntglDb.Persistence.BLite
# or
dotnet add package EntglDb.Persistence.EntityFramework
```

## License

MIT - see [LICENSE](https://github.com/EntglDb/EntglDb.Net/blob/main/LICENSE)
