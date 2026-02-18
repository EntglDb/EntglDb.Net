# EntglDb

<div align="center">

**Peer-to-Peer Data Synchronization Middleware for .NET**

[![.NET Version](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Status
![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)

EntglDb is not a database � it's a **sync layer** that plugs into your existing data store and enables automatic peer-to-peer replication across nodes in a mesh network.

[Architecture](#architecture) � [Quick Start](#quick-start) � [Integration Guide](#integrating-with-your-database) � [Documentation](#documentation)

</div>

---

## ?? Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Key Features](#key-features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Integrating with Your Database](#integrating-with-your-database)
- [Cloud Deployment](#cloud-deployment)
- [Production Features](#production-features)
- [Use Cases](#use-cases)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

---

## ?? Overview

**EntglDb** is a lightweight, embeddable **data synchronization middleware** for .NET. It observes changes in your database via **Change Data Capture (CDC)**, records them in an append-only **Oplog**, and replicates them across nodes connected via a **P2P mesh network**.

Your application continues to read and write to its database as usual. EntglDb works in the background.

> **?? Designed for Local Area Networks (LAN)**  
> Built for trusted environments: offices, retail stores, edge deployments. Cross-platform (Windows, Linux, macOS).

> **?? Cloud Ready**  
> ASP.NET Core hosting with Entity Framework Core support (SQL Server, PostgreSQL, MySQL, SQLite) and OAuth2 authentication for public deployments.

---

## ??? Architecture

```
????????????????????????????????????????????????????????
?                  Your Application                     ?
?  db.Users.InsertAsync(user)                           ?
?  db.Users.Find(u => u.Age > 18)                       ?
????????????????????????????????????????????????????????
               ? uses your DbContext directly
????????????????????????????????????????????????????????
?         Your Database (BLite / EF Core)               ?
?  ??????????????????????????????????????????????????  ?
?  ?  Users    ?  Orders    ?  Products    ?  ...    ?  ?
?  ??????????????????????????????????????????????????  ?
?        ? CDC (Change Data Capture)                     ?
?        ?                                              ?
?  ??????????????????????????????????????????????????  ?
?  ?  EntglDb Sync Engine                           ?  ?
?  ?  � Oplog (append-only hash-chained journal)    ?  ?
?  ?  � Vector Clock (causal ordering)              ?  ?
?  ?  � Conflict Resolution (LWW / Custom Merge)    ?  ?
?  ??????????????????????????????????????????????????  ?
????????????????????????????????????????????????????????
              ? P2P Network (TCP + UDP Discovery)
????????????????????????????????????????????????????????
?           Other Nodes (same setup)                    ?
?  Node-A ?????????? Node-B ?????????? Node-C          ?
????????????????????????????????????????????????????????
```

### Core Concepts

| Concept | Description |
|---------|-------------|
| **Oplog** | Append-only journal of changes, hash-chained per node for integrity |
| **Vector Clock** | Tracks causal ordering � knows who has what across the mesh |
| **CDC** | Change Data Capture � watches your registered collections for local writes |
| **Document Store** | Your bridge class � maps between your entities and the sync engine |
| **Conflict Resolution** | Pluggable strategy (Last-Write-Wins or custom recursive merge) |
| **VectorClockService** | Shared singleton keeping the Vector Clock in sync between CDC and OplogStore |

### Sync Flow

```
Local Write ? CDC Trigger ? OplogEntry Created ? VectorClock Updated
                                                        ?
                                                        ?
                                                  SyncOrchestrator
                                                  (gossip every 2s)
                                                        ?
                                              ?????????????????????
                                              ?                   ?
                                          Push changes       Pull changes
                                          to peers           from peers
                                              ?                   ?
                                              ?                   ?
                                        Remote node          Apply to local
                                        applies via          OplogStore +
                                        ApplyBatchAsync      DocumentStore
```

---

## ? Key Features

### ?? Selective Collection Sync
Only collections registered via `WatchCollection()` are tracked. Your database can have hundreds of tables � only the ones you opt-in participate in replication.

### ?? Interest-Aware Gossip
Nodes advertise which collections they sync. The orchestrator prioritizes peers sharing common interests, reducing unnecessary traffic.

### ?? Offline First
- Read/write operations work offline � they're direct database operations
- Automatic sync when peers reconnect
- Oplog-based gap recovery and snapshot fallback

### ?? Secure Networking
- Noise Protocol handshake with ECDH key exchange
- AES-256 encryption for data in transit
- HMAC authentication
- Brotli compression for bandwidth efficiency

### ?? Conflict Resolution
- **Last Write Wins (LWW)** � default, HLC timestamp-based
- **Recursive Merge** � deep JSON merge for concurrent edits
- **Custom** � implement `IConflictResolver` for your business logic

### ?? Cloud Infrastructure
- ASP.NET Core hosting (Single/Multi cluster modes)
- Entity Framework Core: SQL Server, PostgreSQL, MySQL, SQLite
- OAuth2 JWT authentication

---

## ?? Installation

### Packages

| Package | Purpose |
|---------|---------|
| `EntglDb.Core` | Interfaces, models, conflict resolution (.NET Standard 2.0+) |
| `EntglDb.Persistence` | Base OplogStore, VectorClockService (.NET 8+) |
| `EntglDb.Persistence.BLite` | BLite embedded document DB provider (.NET 10+) |
| `EntglDb.Persistence.EntityFramework` | EF Core provider (.NET 8+) |
| `EntglDb.Network` | TCP sync, UDP discovery, Protobuf protocol (.NET Standard 2.0+) |

```bash
# For BLite (embedded document DB)
dotnet add package EntglDb.Core
dotnet add package EntglDb.Persistence.BLite
dotnet add package EntglDb.Network

# For EF Core (SQL Server, PostgreSQL, etc.)
dotnet add package EntglDb.Core
dotnet add package EntglDb.Persistence.EntityFramework
dotnet add package EntglDb.Network
```

---

## ?? Quick Start

### 1. Define Your Database Context

```csharp
public class MyDbContext : EntglDocumentDbContext
{
    public DocumentCollection<string, Customer> Customers { get; private set; }
    public DocumentCollection<string, Order> Orders { get; private set; }

    public MyDbContext(string dbPath) : base(dbPath) { }
}
```

### 2. Create Your Document Store (the Sync Bridge)

This is where you tell EntglDb **which collections to sync** and **how to map** between your entities and the sync engine:

```csharp
public class MyDocumentStore : BLiteDocumentStore<MyDbContext>
{
    public MyDocumentStore(
        MyDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        ILogger<MyDocumentStore>? logger = null)
        : base(context, configProvider, vectorClockService, logger: logger)
    {
        // Register collections for CDC � only these will be synced
        WatchCollection("Customers", context.Customers, c => c.Id);
        WatchCollection("Orders", context.Orders, o => o.Id);
    }

    // Map incoming sync data back to your entities
    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken ct)
    {
        switch (collection)
        {
            case "Customers":
                var customer = content.Deserialize<Customer>()!;
                customer.Id = key;
                var existing = _context.Customers
                    .Find(c => c.Id == key).FirstOrDefault();
                if (existing != null) _context.Customers.Update(customer);
                else _context.Customers.Insert(customer);
                break;
            case "Orders":
                var order = content.Deserialize<Order>()!;
                order.Id = key;
                var existingOrder = _context.Orders
                    .Find(o => o.Id == key).FirstOrDefault();
                if (existingOrder != null) _context.Orders.Update(order);
                else _context.Orders.Insert(order);
                break;
        }
        await _context.SaveChangesAsync(ct);
    }

    protected override Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken ct)
    {
        object? entity = collection switch
        {
            "Customers" => _context.Customers.Find(c => c.Id == key).FirstOrDefault(),
            "Orders" => _context.Orders.Find(o => o.Id == key).FirstOrDefault(),
            _ => null
        };
        return Task.FromResult(entity != null
            ? (JsonElement?)JsonSerializer.SerializeToElement(entity) : null);
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken ct)
    {
        switch (collection)
        {
            case "Customers": _context.Customers.Delete(key); break;
            case "Orders": _context.Orders.Delete(key); break;
        }
        await _context.SaveChangesAsync(ct);
    }

    protected override Task<IEnumerable<(string Key, JsonElement Content)>>
        GetAllEntitiesAsJsonAsync(string collection, CancellationToken ct)
    {
        IEnumerable<(string, JsonElement)> result = collection switch
        {
            "Customers" => _context.Customers.FindAll()
                .Select(c => (c.Id, JsonSerializer.SerializeToElement(c))),
            "Orders" => _context.Orders.FindAll()
                .Select(o => (o.Id, JsonSerializer.SerializeToElement(o))),
            _ => Enumerable.Empty<(string, JsonElement)>()
        };
        return Task.FromResult(result);
    }
}
```

### 3. Wire It Up

```csharp
var builder = Host.CreateApplicationBuilder();

// Configure the node
builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(
    new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
    {
        NodeId = "node-1",
        TcpPort = 8580,
        AuthToken = "my-cluster-secret"
    }));

// Register EntglDb services
builder.Services
    .AddEntglDbCore()
    .AddEntglDbBLite<MyDbContext, MyDocumentStore>(
        sp => new MyDbContext("mydata.blite"))
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();

await builder.Build().RunAsync();
```

### 4. Use Your Database Normally

```csharp
public class MyService
{
    private readonly MyDbContext _db;

    public MyService(MyDbContext db) => _db = db;

    public async Task CreateCustomer(string name)
    {
        // Write directly � EntglDb handles sync automatically
        await _db.Customers.InsertAsync(
            new Customer { Id = Guid.NewGuid().ToString(), Name = name });
        await _db.SaveChangesAsync();

        // Changes are automatically:
        // 1. Detected via CDC
        // 2. Recorded in the Oplog with HLC timestamp + hash chain
        // 3. Pushed to connected peers via gossip
        // 4. Applied on remote nodes via conflict resolution
    }

    public async Task<List<Customer>> GetYoungCustomers()
    {
        // Read directly from your DB � no EntglDb API
        return _db.Customers.Find(c => c.Age < 30).ToList();
    }
}
```

---

## ?? Integrating with Your Database

If you have an **existing database** and want to add P2P sync:

### Step 1 � Wrap your context

Create a `DbContext` extending `EntglDocumentDbContext` (BLite) or use EF Core directly. This can wrap your existing collections/tables.

```csharp
public class MyExistingDbContext : EntglDocumentDbContext
{
    // Your existing collections
    public DocumentCollection<string, Product> Products { get; private set; }
    public DocumentCollection<string, Inventory> Inventory { get; private set; }
    
    public MyExistingDbContext(string dbPath) : base(dbPath) { }
}
```

### Step 2 � Create a DocumentStore

Extend `BLiteDocumentStore<T>` or implement against EF Core. This is the **bridge** between your data model and the sync engine.

```csharp
public class MyDocumentStore : BLiteDocumentStore<MyExistingDbContext>
{
    public MyDocumentStore(MyExistingDbContext ctx, 
        IPeerNodeConfigurationProvider cfg,
        IVectorClockService vc,
        ILogger<MyDocumentStore>? log = null)
        : base(ctx, cfg, vc, logger: log)
    {
        // Continue to next step...
    }
    
    // Implement abstract methods (see below)...
}
```

### Step 3 � Register only what you need

Call `WatchCollection()` in the constructor for each collection you want to replicate. Everything else is ignored by the sync engine.

```csharp
public MyDocumentStore(...)
    : base(ctx, cfg, vc, logger: log)
{
    // Only these 2 collections will be synced across the mesh
    WatchCollection("Products", ctx.Products, p => p.Id);
    WatchCollection("Inventory", ctx.Inventory, i => i.Id);
    
    // All other collections in your DB are local-only
}
```

### Step 4 � Implement the mapping methods

EntglDb stores data as `JsonElement`. You provide four mapping methods:

| Method | Purpose |
|--------|---------|
| `ApplyContentToEntityAsync` | Write incoming sync data to your entities |
| `GetEntityAsJsonAsync` | Read your entities for outbound sync |
| `RemoveEntityAsync` | Handle remote deletes |
| `GetAllEntitiesAsJsonAsync` | Provide full collection for snapshot sync |

```csharp
protected override async Task ApplyContentToEntityAsync(
    string collection, string key, JsonElement content, CancellationToken ct)
{
    switch (collection)
    {
        case "Products":
            var product = content.Deserialize<Product>()!;
            product.Id = key;
            var existing = _context.Products.Find(p => p.Id == key).FirstOrDefault();
            if (existing != null) _context.Products.Update(product);
            else _context.Products.Insert(product);
            break;
        case "Inventory":
            var inv = content.Deserialize<Inventory>()!;
            inv.Id = key;
            var existingInv = _context.Inventory.Find(i => i.Id == key).FirstOrDefault();
            if (existingInv != null) _context.Inventory.Update(inv);
            else _context.Inventory.Insert(inv);
            break;
    }
    await _context.SaveChangesAsync(ct);
}

protected override Task<JsonElement?> GetEntityAsJsonAsync(
    string collection, string key, CancellationToken ct)
{
    object? entity = collection switch
    {
        "Products" => _context.Products.Find(p => p.Id == key).FirstOrDefault(),
        "Inventory" => _context.Inventory.Find(i => i.Id == key).FirstOrDefault(),
        _ => null
    };
    return Task.FromResult(entity != null
        ? (JsonElement?)JsonSerializer.SerializeToElement(entity) : null);
}

protected override async Task RemoveEntityAsync(
    string collection, string key, CancellationToken ct)
{
    switch (collection)
    {
        case "Products": _context.Products.Delete(key); break;
        case "Inventory": _context.Inventory.Delete(key); break;
    }
    await _context.SaveChangesAsync(ct);
}

protected override Task<IEnumerable<(string Key, JsonElement Content)>>
    GetAllEntitiesAsJsonAsync(string collection, CancellationToken ct)
{
    IEnumerable<(string, JsonElement)> result = collection switch
    {
        "Products" => _context.Products.FindAll()
            .Select(p => (p.Id, JsonSerializer.SerializeToElement(p))),
        "Inventory" => _context.Inventory.FindAll()
            .Select(i => (i.Id, JsonSerializer.SerializeToElement(i))),
        _ => Enumerable.Empty<(string, JsonElement)>()
    };
    return Task.FromResult(result);
}

// Optional: Batch operations for better performance
protected override async Task ApplyContentToEntitiesBatchAsync(
    IEnumerable<(string Collection, string Key, JsonElement Content)> documents,
    CancellationToken ct)
{
    foreach (var (collection, key, content) in documents)
    {
        // Call the single-item method (you can optimize this further)
        await ApplyContentToEntityAsync(collection, key, content, ct);
    }
}
```

**Your existing CRUD code stays unchanged.** EntglDb plugs in alongside it.

### What Happens Under the Hood

```
Your Code: db.Users.InsertAsync(user)
                    ?
                    ?
BLite/EF Core: SaveChangesAsync()
                    ?
                    ? CDC fires (WatchCollection observer)
DocumentStore: CreateOplogEntryAsync()
                    ?
                    ??? OplogEntry written (hash-chained, HLC timestamped)
                    ??? VectorClockService.Update() ? sync sees it immediately
                              ?
                              ?
                    SyncOrchestrator (background, every 2s)
                    ??? Compare VectorClocks with peers
                    ??? Push local changes (interest-filtered)
                    ??? Pull remote changes ? ApplyBatchAsync
                              ?
                              ?
                    Remote DocumentStore: ApplyContentToEntityAsync()
                              ?
                              ?
                    Remote Database: Updated!
```

---

## ?? Cloud Deployment

EntglDb supports ASP.NET Core hosting with Entity Framework Core for cloud deployments.

### Persistence Options

| Database | Best For | Notes |
|----------|----------|-------|
| **SQLite** | Edge computing, embedded | File-based, serverless |
| **SQL Server** | Enterprise | Azure SQL, managed instances |
| **PostgreSQL** | High-performance | JSONB optimization, GIN indexes |
| **MySQL** | Wide compatibility | MariaDB compatible |

### Example: SQL Server with OAuth2

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEntglDbEntityFramework(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=EntglDb;Integrated Security=true");
});

builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.TcpPort = 5001;
    options.RequireAuthentication = true;
    options.OAuth2Authority = "https://auth.example.com";
    options.OAuth2Audience = "entgldb-api";
});

var app = builder.Build();
app.MapHealthChecks("/health");
await app.RunAsync();
```

### Example: PostgreSQL with JSONB

```csharp
builder.Services.AddEntglDbPostgreSql(
    "Host=localhost;Database=EntglDb;Username=app;Password=secret"
);

builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.TcpPort = 5001;
});
```

---

## ??? Production Features

### Configuration

```json
{
  "EntglDb": {
    "KnownPeers": [
      {
        "NodeId": "gateway-1",
        "Address": "192.168.1.10:5000",
        "Type": "StaticRemote"
      }
    ],
    "RetentionHours": 24,
    "SyncIntervalSeconds": 2
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "EntglDb": "Warning"
    }
  }
}
```

### Health Monitoring

```csharp
var healthCheck = new EntglDbHealthCheck(store, syncTracker);
var status = await healthCheck.CheckAsync();

Console.WriteLine($"Database: {status.DatabaseHealthy}");
Console.WriteLine($"Network: {status.NetworkHealthy}");
Console.WriteLine($"Peers: {status.ConnectedPeers}");
```

### Resilience

- **Exponential Backoff**: Automatic retry for unreachable peers
- **Offline Queue**: Buffer local changes when network is down
- **Snapshot Recovery**: Fast catch-up after long disconnects
- **Hash Chain Validation**: Detect and recover from oplog gaps

### Performance

- **VectorClock Cache**: In-memory tracking of node states
- **Brotli Compression**: 70-80% bandwidth reduction
- **Batch Operations**: Group changes for efficient network transfer
- **Interest Filtering**: Only sync collections both peers care about

### Security

- **Noise Protocol Handshake**: XX pattern with ECDH key exchange
- **AES-256 Encryption**: Protect data in transit
- **Auth Tokens**: Shared secret or OAuth2 JWT validation
- **LAN Isolation**: Designed for trusted network environments

---

## ?? Use Cases

### ? Ideal For

- **Retail POS Systems** � Terminals syncing inventory and sales across a store
- **Office Applications** � Shared task lists, calendars, CRM data on LAN
- **Edge Computing** � Distributed sensors and controllers at a facility
- **Offline-First Apps** � Work without internet, sync when connected
- **Multi-Site Replication** � Keep regional databases in sync (over VPN)
- **Existing Database Modernization** � Add P2P sync without rewriting your app

### ? Not Designed For

- **Public internet without HTTPS/VPN** (P2P mesh mode, use ASP.NET Core mode instead)
- **Sub-millisecond consistency requirements** (eventual consistency model, typical convergence < 5s)
- **Unstructured data** (designed for document collections with keys)
- **Append-only event logs** (oplog pruning after 24h retention)

---

## ?? Documentation

### Getting Started

- **[Sample Application](samples/EntglDb.Sample.Console/)** � Complete two-node sync example with interactive CLI
- **[Quick Start Guide](#quick-start)** � 5-minute setup
- **[Integration Guide](#integrating-with-your-database)** � Add sync to existing DB

### Concepts

- **[Architecture & Concepts](docs/architecture.md)** � HLC, Gossip, Vector Clocks, Hash Chains
- **[Conflict Resolution](docs/conflict-resolution.md)** � LWW vs Recursive Merge
- **[Oplog & CDC](docs/oplog-cdc.md)** � How change tracking works

### Deployment

- **[Production Guide](docs/production-hardening.md)** � Configuration, monitoring, best practices
- **[Cloud Deployment](docs/cloud-deployment.md)** � ASP.NET Core, EF Core, OAuth2
- **[Deployment Modes](docs/deployment-modes.md)** � Single vs Multi cluster

### API

- **[API Reference](docs/api-reference.md)** � Complete API documentation
- **[Persistence Providers](docs/persistence-providers.md)** � BLite, EF Core, custom

---

## ?? Examples

See [`samples/EntglDb.Sample.Console/`](samples/EntglDb.Sample.Console/) for a complete working example with:

- **Two-node sync** simulation on different ports
- **Interactive CLI** for testing operations
- **Conflict resolution demo** (switchable LWW/Merge)
- **User and TodoList** entities with full CRUD
- **Health monitoring** and cache inspection
- **Automatic peer discovery** via UDP

Run two instances:
```bash
# Terminal 1
cd samples/EntglDb.Sample.Console
dotnet run -- node-1 8580

# Terminal 2
dotnet run -- node-2 8581

# Create a user on node-1 with command "n"
# Watch it appear on node-2 automatically!
```

---

## ??? Roadmap

- [x] Core P2P mesh networking (v0.1.0)
- [x] Secure networking (ECDH + AES-256) (v0.6.0)
- [x] Conflict resolution strategies (LWW, Recursive Merge) (v0.6.0)
- [x] Hash-chain sync with gap recovery (v0.7.0)
- [x] Brotli compression (v0.7.0)
- [x] Persistence snapshots (v0.8.6)
- [x] ASP.NET Core hosting (v0.8.0)
- [x] Entity Framework Core support (v0.8.0)
- [x] **VectorClockService refactor** (v1.0.0)
- [x] **CDC-aware sync** (v1.0.0)
- [ ] Query optimization & advanced indexing
- [ ] Admin UI / monitoring dashboard
- [ ] Mobile support (Xamarin/MAUI)

---

## ?? Contributing

We welcome contributions! EntglDb is open-source and we'd love your help.

### How to Contribute

1. **Fork the repository**
2. **Create a feature branch** (`git checkout -b feature/amazing-feature`)
3. **Make your changes** with clear commit messages
4. **Add tests** for new functionality
5. **Ensure all tests pass** (`dotnet test`)
6. **Submit a Pull Request**

### Development Setup

```bash
# Clone the repository
git clone https://github.com/EntglDb/EntglDb.Net.git
cd EntglDb.Net

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run all tests (69 tests)
dotnet test

# Run sample
cd samples/EntglDb.Sample.Console
dotnet run
```

### Areas We Need Help

- ?? **Bug Reports** � Found an issue? Let us know!
- ?? **Documentation** � Improve guides and examples
- ? **Features** � Implement items from the roadmap
- ?? **Testing** � Add integration and performance tests
- ?? **Samples** � Build example applications

### Code of Conduct

Be respectful, inclusive, and constructive. We're all here to learn and build great software together.

---

## ?? License

EntglDb is licensed under the **MIT License**.

```
MIT License

Copyright (c) 2026 MrDevRobot

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software...
```

See [LICENSE](LICENSE) file for full details.

---

## Give it a Star! ?

If you find EntglDb useful, please **give it a star** on GitHub! It helps others discover the project and motivates us to keep improving it.

<div align="center">

### [? Star on GitHub](https://github.com/EntglDb/EntglDb.Net)

**Thank you for your support!** ??

**Built with ?? for the .NET community**

[Report Bug](https://github.com/EntglDb/EntglDb.Net/issues) � [Request Feature](https://github.com/EntglDb/EntglDb.Net/issues) � [Discussions](https://github.com/EntglDb/EntglDb.Net/discussions)

</div>
