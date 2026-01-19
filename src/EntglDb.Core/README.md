# EntglDb.Core

Core abstractions and logic for **EntglDb**, a lightweight peer-to-peer mesh database for .NET.

## What's Included

This package provides the core functionality for EntglDb:

- **Type-Safe API**: Generic `Collection<T>` with LINQ support
- **Document Storage**: Abstract interfaces for persistence
- **Hybrid Logical Clocks (HLC)**: Distributed timestamp management
- **Configuration System**: `EntglDbOptions` for flexible setup
- **Production Features**:
  - Document caching (LRU)
  - Offline operation queue
  - Health monitoring
  - Retry policies
  - Exception hierarchy

## Installation

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Persistence.Sqlite
```

## Quick Start

```csharp
using EntglDb.Core;

// Define your model
public class Product
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// Use type-safe collections
var products = database.Collection<Product>();
await products.Put(new Product { Name = "Laptop", Price = 999.99m });

// Query with LINQ
var results = await products.Find(p => p.Price > 500);
```

## Documentation

- **[Complete Documentation](https://github.com/EntglDb/EntglDb.Net)**
- **[API Reference](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/api-reference.md)**
- **[Production Hardening](https://github.com/EntglDb/EntglDb.Net/blob/main/docs/production-hardening.md)**

## Related Packages

- **EntglDb.Network** - P2P networking (UDP discovery, TCP sync, Gossip)
- **EntglDb.Persistence.Sqlite** - SQLite storage provider

## License

MIT - see [LICENSE](https://github.com/EntglDb/EntglDb.Net/blob/main/LICENSE)
