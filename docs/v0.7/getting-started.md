# Getting Started (v0.7.0)

## Installation

EntglDb is available as a set of NuGet packages.

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

### EntglStudio (New!)

EntglStudio is a standalone GUI tool for managing your EntglDb nodes and data.

*   [**Download EntglStudio**](https://github.com/EntglDb/EntglDb.Net/releases)

## Basic Usage

### 1. Initialize the Store
Use `SqlitePeerStore` for persistence. Supported on Windows, Linux, and macOS.

```csharp
using EntglDb.Core;
using EntglDb.Core.Sync;
using EntglDb.Persistence.Sqlite;
using EntglDb.Network.Security;

// Choose conflict resolver (v0.6.0+)
var resolver = new RecursiveNodeMergeConflictResolver(); // OR LastWriteWinsConflictResolver()

var store = new SqlitePeerStore("Data Source=my-node.db", logger, resolver);
// Automatically creates tables on first run
```

### 2. Configure Networking (with Optional Security)
Use `AddEntglDbNetwork` extension method to register services.

```csharp
var services = new ServiceCollection();
string myNodeId = "node-1";
int port = 5001;
string authToken = "my-secret-cluster-key";

services.AddSingleton<IPeerStore>(store);

// Optional: Enable encryption (v0.6.0+)
services.AddSingleton<IPeerHandshakeService, SecureHandshakeService>();

services.AddEntglDbNetwork(myNodeId, port, authToken);
```

### 3. Start the Node

```csharp
var provider = services.BuildServiceProvider();
var node = provider.GetRequiredService<EntglDbNode>();

node.Start();
```

### 4. CRUD Operations
Interact with data using `PeerDatabase`.

```csharp
var db = new PeerDatabase(store, "my-node-id"); // Node ID used for HLC clock
await db.InitializeAsync();

var users = db.Collection("users");

// Put
await users.Put("user-1", new { Name = "Alice", Age = 30 });

// Get
var user = await users.Get<User>("user-1");

// Query
var results = await users.Find<User>(u => u.Age > 20);
```

## What's New in v0.7.0

### 📦 Efficient Networking
- **Brotli Compression**: Data is automatically compressed significantly reducing bandwidth usage.
- **Protocol v4**: Enhanced framing and security negotiation.

## What's New in v0.6.0

### 🔐 Secure Networking
Protect your data in transit with:
- **ECDH** key exchange
- **AES-256-CBC** encryption
- **HMAC-SHA256** authentication

[Learn more about Security →](security.html)

### 🔀 Advanced Conflict Resolution
Choose your strategy:
- **Last Write Wins** - Simple, fast, timestamp-based
- **Recursive Merge** - Intelligent JSON merging with array ID detection

[Learn more about Conflict Resolution →](conflict-resolution.html)

### 🎯 Multi-Target Framework Support
- `netstandard2.0` - Maximum compatibility
- `net6.0` - Modern features
- `net8.0` - Latest performance optimizations

## Next Steps

- [Architecture Overview](architecture.html)
- [Security Configuration](security.html)
- [Conflict Resolution Strategies](conflict-resolution.html)
- [Production Hardening](production-hardening.html)
- [API Reference](api-reference.html)
