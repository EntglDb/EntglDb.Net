# EntglDb Sample Console Application

This sample demonstrates the core features of EntglDb, a distributed peer-to-peer database with automatic synchronization.

## Features Demonstrated

### 🔑 Primary Keys & Auto-Generation
- Automatic GUID generation for entities
- Convention-based key detection (`Id` property)
- `[PrimaryKey]` attribute support

### 🎯 Generic Type-Safe API
- `Collection<T>()` for compile-time type safety
- Keyless `Put(entity)` with auto-key extraction
- IntelliSense-friendly operations

### 🔍 LINQ Query Support
- Expression-based queries
- Paging and sorting
- Complex predicates (>, >=, ==, !=, nested properties)

### 🌐 Network Synchronization
- UDP peer discovery
- TCP synchronization
- Automatic conflict resolution (Last-Write-Wins)

## Running the Sample

### Single Node

```bash
dotnet run
```

### Multi-Node (Peer-to-Peer)

Terminal 1:
```bash
dotnet run -- --node-id node1 --tcp-port 5001 --udp-port 6001
```

Terminal 2:
```bash
dotnet run -- --node-id node2 --tcp-port 5002 --udp-port 6002
```

Terminal 3:
```bash
dotnet run -- --node-id node3 --tcp-port 5003 --udp-port 6003
```

Changes made on any node will automatically sync to all peers!

## Available Commands

| Command | Description |
|---------|-------------|
| `p` | Put Alice and Bob (auto-generated IDs) |
| `g` | Get user by ID (prompts for ID) |
| `d` | Delete user by ID (prompts for ID) |
| `n` | Create new user with auto-generated ID |
| `s` | Spam 5 users with auto-generated IDs |
| `c` | Count total documents |
| `f` | Demo various Find queries |
| `f2` | Demo Find with paging (skip/take) |
| `a` | Demo auto-generated primary keys |
| `t` | Demo generic typed API |
| `l` | List active peers |
| `q` | Quit |

## Example Session

```
--- Started node1 on Port 5001 ---
Commands: [p]ut, [g]et, [d]elete, [l]ist peers, [q]uit, [f]ind
          [n]ew (auto-generate), [s]pam (5x auto), [c]ount
          [a]uto-keys (demo), [t]yped (demo generic API)

> p
Put Alice (Id: 3fa85f64...) and Bob (Id: 7c9e6679...)

> c
Total Documents: 2

> f
Query: Age > 28
Found: Alice (30)

> a
=== Auto-Generated Primary Keys Demo ===
Created: AutoUser1 with auto-generated Id: 9b2c3d4e...
Created: AutoUser2 with auto-generated Id: 1a2b3c4d...
Retrieved: AutoUser1 (Age: 25)

> l
Active Peers:
- node2 at 127.0.0.1:5002
- node3 at 127.0.0.1:5003
```

## Code Highlights

### Entity Definition

```csharp
using EntglDb.Core.Metadata;

public class User
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    
    public string? Name { get; set; }
    
    [Indexed]
    public int Age { get; set; }
    
    public Address? Address { get; set; }
}
```

### Using the API

```csharp
// Get typed collection
var users = db.Collection<User>();

// Auto-generates Id
var user = new User { Name = "Alice", Age = 30 };
await users.Put(user);
Console.WriteLine(user.Id); // "3fa85f64-5717-4562-b3fc-2c963f66afa6"

// Retrieve by ID
var retrieved = await users.Get(user.Id);

// Query with LINQ
var results = await users.Find(u => u.Age > 30);

// Paging
var page = await users.Find(u => true, skip: 10, take: 5);
```

## Architecture

- **Storage**: SQLite with HLC timestamps
- **Sync**: TCP for data transfer, UDP for discovery
- **Conflict Resolution**: Last-Write-Wins based on Hybrid Logical Clocks
- **Serialization**: System.Text.Json

## Learn More

- [API Reference](../../docs/api-reference.md)
- [Architecture](../../docs/architecture.md)
- [Getting Started](../../docs/getting-started.md)
