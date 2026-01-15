# API Reference

## PeerDatabase

The entry point for interacting with the database.

```csharp
public class PeerDatabase : IPeerDatabase
{
    public PeerDatabase(IPeerStore store, string nodeId = "local");
    public IPeerCollection Collection(string name);
}
```

## IPeerCollection

Represents a collection of documents (like a Table or Container).

### Methods

#### `Put(string key, object document)`
Inserts or updates a document.
- **key**: Unique identifier.
- **document**: Any POCO or anonymous object. Serialized to JSON.

#### `Get<T>(string key)`
Retrieves a document by key.
- Returns `default(T)` if not found.

#### `Delete(string key)`
Marks a document as deleted (Soft Delete / Tombstone).

#### `Find<T>(Expression<Func<T, bool>> predicate, ...)`
Queries documents using a LINQ expression.

```csharp
// Simple
await col.Find<User>(u => u.Age > 18);

// Paged
await col.Find<User>(
    u => u.IsActive, 
    skip: 10, 
    take: 10, 
    orderBy: "Name", 
    ascending: true
);
```

### Querying capabilities
The following operators are supported in LINQ expressions:
- `==`, `!=`, `>`, `<`, `>=`, `<=`
- `&&`, `||`
- `string.Contains` (maps to SQL LIKE)
