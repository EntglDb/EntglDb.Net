# Getting Started

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
using EntglDb.Persistence.Sqlite;

var store = new SqlitePeerStore("Data Source=my-node.db");
// Automatically creates tables on first run
```

### 2. Configure Networking
Use `AddEntglDbNetwork` extension method to register services.

```csharp
var services = new ServiceCollection();
string myNodeId = "node-1";
int port = 5001;
string authToken = "my-secret-cluster-key";

services.AddSingleton<IPeerStore>(store);
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
