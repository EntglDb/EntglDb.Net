# API Reference

## PeerDatabase

The entry point for interacting with the database.

```csharp
public class PeerDatabase : IPeerDatabase
{
    public PeerDatabase(IPeerStore store, string nodeId = "local");
    
    // Non-generic collection access
    public IPeerCollection Collection(string name);
    
    // Generic collection access (type-safe)
    public IPeerCollection<T> Collection<T>(string? customName = null);
}
```

### Collection Naming Convention

When using `Collection<T>()`, the collection name defaults to `typeof(T).Name.ToLowerInvariant()`. You can change this globally using `EntglDbMapper`.

```csharp
// Uses default collection name "user"
var users = db.Collection<User>();

// Custom name override
var users = db.Collection<User>("custom_users");
```

## IPeerCollection<T>

Represents a collection of documents (like a Table or Container).

```csharp
public interface IPeerCollection<T> : IPeerCollection
{
    // Single Operations
    Task Put(string key, T document, CancellationToken cancellationToken = default);
    Task<T> Get(string key, CancellationToken cancellationToken = default);
    Task Delete(string key, CancellationToken cancellationToken = default);

    // Batch Operations
    Task PutMany(IEnumerable<T> documents, CancellationToken cancellationToken = default);
    Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    // Queries
    Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> Count(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
}
```

### Batch Operations

For bulk inserts or deletes, use `PutMany` and `DeleteMany` for better performance and atomicity.

```csharp
var users = new List<User> { ... };
await db.Collection<User>().PutMany(users);
```

### Count

Get the total number of documents matching a filter efficiently (database-side).

```csharp
int total = await db.Collection<User>().Count();
int active = await db.Collection<User>().Count(u => u.IsActive);
```

## Global Configuration (EntglDbMapper)

You can configure entity mappings globally instead of using attributes.

```csharp
EntglDbMapper.Global.Entity<Product>()
    .Collection("products_v2")
    .Index(p => p.Price)
    .Index(p => p.Category);
```

## Primary Keys and Indexes

### Attributes

```csharp
using EntglDb.Core.Metadata;

public class User
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    
    [Indexed]
    public int Age { get; set; }
}
```

### Indexed Fields

Indexes improve query performance (e.g., `Find(u => u.Age > 18)`).
You can define them via `[Indexed]` attribute or `EntglDbMapper`.

> **Note**: EntglDb automatically creates SQLite indexes for these properties.

## Exceptions

EntglDb throws specific exceptions for better error handling:

- `EntglDbException` (Base class)
- `DocumentNotFoundException`
- `EntglDbConcurrencyException`
- `TimeoutException`
- `NetworkException`
- `PersistenceException`

```csharp
try
{
    await db.Collection<User>().Get("invalid-id");
}
catch (DocumentNotFoundException ex)
{
    Console.WriteLine($"Document {ex.Key} not found in {ex.Collection}");
}
```

## Querying

Supported LINQ operators:
- Equality: `==`, `!=`
- Comparison: `>`, `<`, `>=`, `<=`
- Boolean: `&&`, `||`
- String: `Contains`, `StartsWith`, `EndsWith` (Mapped to SQL `LIKE`)
- Collections: `Contains` (IN clause)

```csharp
// Find users in list of IDs
var ids = new[] { "1", "2" };
await col.Find(u => ids.Contains(u.Id));
```
