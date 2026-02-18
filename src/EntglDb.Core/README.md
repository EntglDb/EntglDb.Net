# EntglDb.Core

Core abstractions and logic for **EntglDb**, a peer-to-peer data synchronization middleware for .NET.

## What Is EntglDb?

EntglDb is **not** a database — it's a sync layer that plugs into your existing data store (BLite, EF Core) and enables automatic P2P replication across nodes in a mesh network. Your application reads and writes to its database as usual; EntglDb handles synchronization in the background.

## What's In This Package

- **Interfaces**: `IDocumentStore`, `IOplogStore`, `IVectorClockService`, `IConflictResolver`
- **Models**: `OplogEntry`, `Document`, `HlcTimestamp`, `VectorClock`
- **Conflict Resolution**: `LastWriteWinsConflictResolver`, `RecursiveNodeMergeConflictResolver`
- **Production Features**: Document caching (LRU), offline queue, health monitoring, retry policies

## Installation

```bash
# Pick a persistence provider
dotnet add package EntglDb.Persistence.BLite          # Embedded document DB
dotnet add package EntglDb.Persistence.EntityFramework # EF Core (SQL Server, PostgreSQL, etc.)

# Add networking
dotnet add package EntglDb.Network
```

## Quick Start

```csharp
// 1. Define your DbContext
public class MyDbContext : EntglDocumentDbContext
{
    public DocumentCollection<string, User> Users { get; private set; }
    public MyDbContext(string path) : base(path) { }
}

// 2. Create your DocumentStore (the sync bridge)
public class MyDocumentStore : BLiteDocumentStore<MyDbContext>
{
    public MyDocumentStore(MyDbContext ctx, IPeerNodeConfigurationProvider cfg,
        IVectorClockService vc, ILogger<MyDocumentStore>? log = null)
        : base(ctx, cfg, vc, logger: log)
    {
        WatchCollection("Users", ctx.Users, u => u.Id);
    }

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken ct)
    {
        var user = content.Deserialize<User>()!;
        user.Id = key;
        var existing = _context.Users.Find(u => u.Id == key).FirstOrDefault();
        if (existing != null) _context.Users.Update(user);
        else _context.Users.Insert(user);
        await _context.SaveChangesAsync(ct);
    }
    // ... implement other abstract methods
}

// 3. Register and use
builder.Services.AddEntglDbCore()
    .AddEntglDbBLite<MyDbContext, MyDocumentStore>(
        sp => new MyDbContext("data.blite"))
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();
```

## Key Concepts

| Concept | Description |
|---------|-------------|
| **CDC** | Change Data Capture — watches collections registered via `WatchCollection()` |
| **Oplog** | Append-only hash-chained journal of changes per node |
| **VectorClock** | Tracks causal ordering across the mesh |
| **DocumentStore** | Your bridge between entities and the sync engine |

## Architecture

```
Your App ? DbContext.SaveChangesAsync()
               ?
               ? CDC Trigger
           DocumentStore.CreateOplogEntryAsync()
               ?
               ??? OplogEntry (hash-chained, HLC timestamped)
               ??? VectorClockService.Update()
                       ?
                       ?
               SyncOrchestrator (background)
               ??? Push to peers
               ??? Pull from peers ? ApplyBatchAsync
```

## Related Packages

- **EntglDb.Persistence.BLite** — BLite embedded provider (.NET 10+)
- **EntglDb.Persistence.EntityFramework** — EF Core provider (.NET 8+)
- **EntglDb.Network** — P2P networking (UDP discovery, TCP sync, Gossip)

## Documentation

- **[Complete Documentation](https://github.com/EntglDb/EntglDb.Net)**
- **[Sample Application](https://github.com/EntglDb/EntglDb.Net/tree/main/samples/EntglDb.Sample.Console)**
- **[Integration Guide](https://github.com/EntglDb/EntglDb.Net#integrating-with-your-database)**

## License

MIT — see [LICENSE](https://github.com/EntglDb/EntglDb.Net/blob/main/LICENSE)
