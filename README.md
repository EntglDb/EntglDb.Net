# EntglDb

<div align="center">

**A Lightweight Peer-to-Peer Database for .NET**

[![.NET Version](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
## Status
![Version](https://img.shields.io/badge/version-0.7.0-blue.svg)
![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)

**Latest Release**: v0.7.0 (Brotli Compression, Uniform Versioning)

EntglDb is a mesh database...
[Features](#features) • [Quick Start](#quick-start) • [Documentation](#documentation) • [Examples](#examples) • [Contributing](#contributing)

</div>

---

## 📋 Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Production Features](#production-features)
- [Use Cases](#use-cases)
- [Documentation](#documentation)
- [Examples](#examples)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)
- [Give it a Star](#give-it-a-star-⭐)

---

## 🎯 Overview

**EntglDb** (formerly PeerDb) is a lightweight, embeddable **Peer-to-Peer (P2P) database** for .NET that enables you to build **local-first**, **offline-capable** applications with automatic data synchronization across nodes in a mesh network.

> **🏠 Designed for Local Area Networks (LAN)**  
> EntglDb is specifically built for **trusted LAN environments** such as offices, homes, retail stores, and edge computing deployments. It is **cross-platform** (Windows, Linux, macOS) and optimized for scenarios where nodes operate on the same local network.

> **⚠️ NOT for Public Internet**  
> EntglDb is **NOT** designed for public internet deployment without additional security measures (TLS, authentication, firewall rules). See [Security Considerations](docs/architecture.md#security-disclaimer).

---

## ✨ Key Features

### 🔄 Mesh Networking
- **Automatic Peer Discovery** via UDP broadcast
- **TCP-based Synchronization** between nodes
- **Gossip Protocol** for efficient update propagation
- **No Central Server** required - fully decentralized

### 📴 Offline First
- **Local SQLite Database** on every node
- **Read/Write operations work offline**
- **Automatic sync** when peers reconnect
- **Conflict resolution** strategies (Last Write Wins, Recursive Merge)

### 🔐 Secure Networking (v0.6.0)
- **ECDH Key Exchange** for session keys
- **AES-256-CBC Encryption** for data in transit
- **HMAC-SHA256 Authentication** to prevent tampering
- **Optional secure mode** for sensitive deployments
- **Brotli Compression** (v0.7.0) for efficient bandwidth usage

### 🔀 Advanced Conflict Resolution (v0.6.0)
- **Last Write Wins (LWW)** - Simple timestamp-based resolution
- **Recursive Merge** - Intelligent JSON merging with array ID detection
- **Runtime switchable** via configuration
- **Visual demo** in UI samples

### 🎯 Type-Safe API
- **Generic Collection API** with LINQ support
- **Auto-generated primary keys** using attributes
- **Indexed properties** for optimized queries
- **Expression-based filtering** `await users.Find(u => u.Age > 30)`

### 🛡️ Production Ready (v0.5.0+)
- **Configuration System** (appsettings.json support)
- **Resilience**: Retry policies, offline queue, error handling
- **Performance**: LRU cache, batch operations, WAL mode, net8.0 optimizations
- **Monitoring**: Health checks, sync status, diagnostics
- **Reliability**: Database backup, integrity checks, corruption detection

### 🌍 Cross-Platform
- **Windows** (10+, Server 2019+)
- **Linux** (Ubuntu, Debian, RHEL, Alpine)
- **macOS** (11+)

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Your Application                     │
└─────────────────────┬───────────────────────────────────┘
                      │
        ┌─────────────▼─────────────┐
        │      EntglDb.Core         │  Type-safe API
        │   (PeerDatabase, Cache)   │  Configuration
        └─────────────┬─────────────┘  Offline Queue
                      │
        ┌─────────────▼─────────────┐
        │  EntglDb.Persistence.     │  SQLite Storage
        │       Sqlite              │  WAL Mode
        └─────────────┬─────────────┘  Backup/Restore
                      │
        ┌─────────────▼─────────────┐
        │    EntglDb.Network        │  UDP Discovery
        │  (P2P Synchronization)    │  TCP Sync
        └───────────────────────────┘  Gossip Protocol
```

### Core Concepts

- **Hybrid Logical Clocks (HLC)**: Provides total ordering of events across distributed nodes
- **Last-Write-Wins (LWW)**: Automatic conflict resolution based on HLC timestamps
- **Anti-Entropy**: Peers exchange and reconcile differences when they connect
- **Gossip Protocol**: Updates propagate exponentially through the network

---

## 📦 Installation

### NuGet Packages

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

### Requirements
- **.NET 10.0+** Runtime
- **SQLite** (included via Microsoft.Data.Sqlite)

---

## 🚀 Quick Start

### 1. Basic Setup

```csharp
using EntglDb.Core;
using EntglDb.Persistence.Sqlite;
using EntglDb.Network;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configuration
services.Configure<EntglDbOptions>(options =>
{
    options.Network.TcpPort = 5000;
    options.Persistence.DatabasePath = "data/myapp.db";
    options.Persistence.EnableWalMode = true;
});

// Register EntglDb services
services.AddSingleton<IPeerStore>(sp => 
    new SqlitePeerStore("Data Source=myapp.db"));
services.AddEntglDbNetwork("my-node-id", 5000, "shared-secret");

var provider = services.BuildServiceProvider();
var node = provider.GetRequiredService<EntglDbNode>();
node.Start();

var db = new PeerDatabase(store, "my-node-id");
await db.InitializeAsync();
```

### 2. Define Your Models

```csharp
public class Product
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    
    public string Name { get; set; }
    
    [Indexed]
    public decimal Price { get; set; }
    
    public int Stock { get; set; }
}
```

### 3. Use Type-Safe Collections

```csharp
// Get typed collection
var products = db.Collection<Product>();

// Insert (auto-generates Id)
var product = new Product 
{ 
    Name = "Laptop", 
    Price = 999.99m, 
    Stock = 10 
};
await products.Put(product);

// Query with LINQ
var expensive = await products.Find(p => p.Price > 500);

// Get by key
var item = await products.Get(product.Id);

// Delete
await products.Delete(product.Id);
```

### 4. Automatic Synchronization

Multiple nodes running the same code will **automatically discover each other** on the LAN and **synchronize data** in real-time!

```bash
# Terminal 1
dotnet run -- node-1 5001

# Terminal 2  
dotnet run -- node-2 5002

# Changes on node-1 automatically sync to node-2!
```

---

## 🛡️ Production Features

EntglDb v0.2.0+ includes production-hardening features for LAN deployments:

### Configuration
```json
{
  "EntglDb": {
    "Network": {
      "TcpPort": 5000,
      "RetryAttempts": 3,
      "ConnectionTimeoutMs": 5000
    },
    "Persistence": {
      "DatabasePath": "data/entgldb.db",
      "EnableWalMode": true,
      "EnableAutoBackup": true,
      "CacheSizeMb": 50
    },
    "Sync": {
      "EnableOfflineQueue": true,
      "BatchSize": 100
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

### Caching & Performance
```csharp
var cache = new DocumentCache(maxSizeMb: 50);

// Check cache first, then database
var doc = cache.Get("products", "prod-123") 
    ?? await store.GetDocumentAsync("products", "prod-123");
```

### Backup & Recovery
```csharp
// Create backup
await store.BackupAsync("backups/backup-20260115.db");

// Check database integrity
var isHealthy = await store.CheckIntegrityAsync();
```

See [Production Hardening Guide](docs/production-hardening.md) for details.

---

## 💼 Use Cases

### ✅ Ideal For
- **Retail Point-of-Sale Systems** - Multiple terminals syncing inventory
- **Office Applications** - Shared data across workstations
- **Home Automation** - IoT devices on home network
- **Edge Computing** - Distributed sensors and controllers
- **Offline-First Apps** - Applications that must work without internet
- **Development/Testing** - Distributed system prototyping

### ❌ Not Recommended For
- **Public Internet Applications** (without significant security enhancements)
- **Multi-Tenant SaaS** platforms
- **High-Security Environments** (medical, financial without additional encryption)
- **Mobile Apps** over cellular (designed for LAN/WiFi)

---

## 📚 Documentation

- **[Architecture & Concepts](docs/architecture.md)** - Deep dive into HLC, Gossip, and sync
- **[API Reference](docs/api-reference.md)** - Complete API documentation
- **[Production Hardening](docs/production-hardening.md)** - Configuration, monitoring, best practices
- **[LAN Deployment Guide](docs/deployment-lan.md)** - Platform-specific deployment instructions
- **[Sample Application](samples/EntglDb.Sample.Console/)** - Complete working example

---

## 🎯 Examples

### Automatic Key Generation

```csharp
public class User
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    
    public string Name { get; set; }
}

var users = db.Collection<User>();
var user = new User { Name = "Alice" };
await users.Put(user);  // Id auto-generated as GUID
```

### LINQ Queries

```csharp
// Find users older than 30
var results = await users.Find(u => u.Age > 30);

// Find users in a specific city
var localUsers = await users.Find(u => u.City == "Rome");
```

### Offline Operation

```csharp
// Queue operations while offline
if (!isOnline)
{
    queue.Enqueue(new PendingOperation 
    { 
        Type = "put", 
        Collection = "orders", 
        Data = order 
    });
}

// Flush when back online
var (success, failed) = await queue.FlushAsync(executor);
```

### Batch Operations
Added `PutMany` and `DeleteMany` for efficient bulk processing.

```csharp
var users = db.Collection<User>();
var list = new List<User> { new User("A"), new User("B") };

// Efficient batch insert
await users.PutMany(list);

// Efficient batch delete
await users.DeleteMany(new[] { "id-1", "id-2" });
```

### Global Configuration (EntglDbMapper)

```csharp
EntglDbMapper.Global.Entity<Product>()
    .Collection("products_v2")
    .Index(p => p.Price)
    .Index(p => p.Category);
```

---

## 🗺️ Roadmap

- [x] Core P2P mesh networking
- [x] Type-safe generic API
- [x] Unit tests (33 passing)
- [x] Production hardening (v0.2.0)
- [x] LAN deployment documentation
- [x] **Secure networking** with ECDH + AES-256 (v0.6.0)
- [x] **Conflict resolution strategies** - LWW & Recursive Merge (v0.6.0)
- [x] **Multi-target framework** support (netstandard2.0, net6.0, net8.0)
- [x] **Performance benchmarks** and regression tests
- [ ] Merkle Trees for efficient sync
- [ ] Query optimization & advanced indexing
- [x] **Compressed sync protocol** (Brotli) (v0.7.0)
- [ ] Admin UI / monitoring dashboard

---

## 🤝 Contributing

We welcome contributions! EntglDb is an open-source project and we'd love your help.

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
cd EntglDb

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Run sample
cd samples/EntglDb.Sample.Console
dotnet run
```

### Areas We Need Help

- 🐛 **Bug Reports** - Found an issue? Let us know!
- 📝 **Documentation** - Improve guides and examples
- ✨ **Features** - Implement items from the roadmap
- 🧪 **Testing** - Add integration and performance tests
- 🎨 **Samples** - Build example applications

### Code of Conduct

Be respectful, inclusive, and constructive. We're all here to learn and build great software together.

---

## 📄 License

EntglDb is licensed under the **MIT License**.

```
MIT License

Copyright (c) 2026 MrDevRobot

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files...
```

See [LICENSE](LICENSE) file for full details.

---

## Give it a Star! ⭐

If you find EntglDb useful, please **give it a star** on GitHub! It helps others discover the project and motivates us to keep improving it.

<div align="center">

### [⭐ Star on GitHub](https://github.com/EntglDb/EntglDb.Net)

**Thank you for your support!** 🙏

</div>

---

<div align="center">

**Built with ❤️ for the .NET community**

[Report Bug](https://github.com/EntglDb/EntglDb.Net/issues) • [Request Feature](https://github.com/EntglDb/EntglDb.Net/issues) • [Discussions](https://github.com/EntglDb/EntglDb.Net/discussions)

</div>
