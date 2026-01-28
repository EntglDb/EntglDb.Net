# Getting Started (v0.9.0)

## Installation

EntglDb is available as a set of NuGet packages for .NET 8.0, .NET 6.0, and .NET Standard 2.0.

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

### Cloud & Enterprise Packages

For ASP.NET Core hosting and enterprise database support:

```bash
# ASP.NET Core hosting
dotnet add package EntglDb.AspNet

# Entity Framework Core (SQL Server, MySQL, SQLite)
dotnet add package EntglDb.Persistence.EntityFramework

# PostgreSQL with JSONB optimization
dotnet add package EntglDb.Persistence.PostgreSQL
```

### EntglStudio (New!)

EntglStudio is a standalone GUI tool for managing your EntglDb nodes and data.

*   [**Download EntglStudio**](https://github.com/EntglDb/EntglDb.Net/releases)

## Requirements

- **.NET 8.0+ Runtime** (recommended) or .NET 6.0+
- **SQLite** (included via Microsoft.Data.Sqlite)
- **PostgreSQL 12+** (optional, for PostgreSQL persistence)
- **SQL Server 2016+** (optional, for SQL Server persistence)

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

## ASP.NET Core Deployment (v0.8.0+)

### Single Cluster Mode (Recommended)

Perfect for production deployments with dedicated database servers:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Use PostgreSQL for production
builder.Services.AddEntglDbPostgreSql(
    builder.Configuration.GetConnectionString("EntglDb"));

// Configure single cluster
builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.NodeId = "server-01";
    options.TcpPort = 5001;
    options.RequireAuthentication = true;
    options.OAuth2Authority = "https://auth.example.com";
});

var app = builder.Build();

app.MapHealthChecks("/health");
app.Run();
```

### Multi-Cluster Mode

For multi-tenant scenarios or shared hosting:

```csharp
builder.Services.AddEntglDbAspNetMultiCluster(options =>
{
    options.BasePort = 5001;
    options.ClusterCount = 10;
    options.RequireAuthentication = true;
    options.NodeIdTemplate = "server-{ClusterId}";
});
```

See [Deployment Modes](deployment-modes.md) for detailed comparison.

## What's New in v0.9.0

### 🚀 Production Enhancements
- **Improved ASP.NET Core Sample**: Enhanced error handling and better examples
- **EF Core Stability**: Fixed runtime issues for all persistence providers
- **Sync Refinements**: More reliable synchronization across all deployment modes

### 📸 Snapshots (v0.8.6)
- **Fast Reconnection**: Peers resume sync from the last known state
- **Optimized Recovery**: Prevents re-processing of already applied operations
- **Automatic Management**: Snapshot metadata tracked per peer

See [CHANGELOG](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md) for complete version history.

## What's New in v0.8.0

### ☁️ Cloud Infrastructure
- **ASP.NET Core Hosting**: Single and Multi-cluster deployment modes
- **Multi-Database Support**: SQL Server, PostgreSQL, MySQL, SQLite via EF Core
- **PostgreSQL Optimization**: JSONB storage with GIN indexes
- **OAuth2 JWT Authentication**: Secure cloud deployments
- **Health Checks**: Production monitoring and observability

[Learn more about Cloud Deployment →](deployment-modes.md)

## What's New in v0.7.0

### 📦 Efficient Networking
- **Brotli Compression**: Data is automatically compressed, significantly reducing bandwidth usage
- **Protocol v4**: Enhanced framing and security negotiation

## What's New in v0.6.0

### 🔐 Secure Networking
Protect your data in transit with:
- **ECDH** key exchange
- **AES-256-CBC** encryption
- **HMAC-SHA256** authentication

[Learn more about Security →](security.md)

### 🔀 Advanced Conflict Resolution
Choose your strategy:
- **Last Write Wins** - Simple, fast, timestamp-based
- **Recursive Merge** - Intelligent JSON merging with array ID detection

[Learn more about Conflict Resolution →](conflict-resolution.md)

### 🎯 Multi-Target Framework Support
- `netstandard2.0` - Maximum compatibility
- `net6.0` - Modern features
- `net8.0` - Latest performance optimizations

## Next Steps

- [Architecture Overview](architecture.html) - Understand HLC, Gossip Protocol, and mesh networking
- [Persistence Providers](persistence-providers.html) - Choose the right database for your deployment
- [Deployment Modes](deployment-modes.html) - Single vs Multi-cluster strategies
- [Security Configuration](security.html) - Encryption and authentication
- [Conflict Resolution Strategies](conflict-resolution.html) - LWW vs Recursive Merge
- [Production Hardening](production-hardening.html) - Best practices and monitoring
- [API Reference](api-reference.html) - Complete API documentation
