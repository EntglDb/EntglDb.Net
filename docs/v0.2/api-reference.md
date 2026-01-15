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

When using `Collection<T>()`, the collection name defaults to `typeof(T).Name.ToLowerInvariant()`:

```csharp
// Uses collection name "user"
var users = db.Collection<User>();

// Custom name
var users = db.Collection<User>("custom_users");
```

## IPeerCollection (Non-Generic)

Represents a collection of documents (like a Table or Container).

```csharp
public interface IPeerCollection
{
    string Name { get; }
    Task Put(string key, object document, CancellationToken cancellationToken = default);
    Task<T> Get<T>(string key, CancellationToken cancellationToken = default);
    Task Delete(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}
```

## IPeerCollection<T> (Generic, Type-Safe)

Represents a collection of documents (like a Table or Container).

```csharp
public interface IPeerCollection<T> : IPeerCollection
{
    Task Put(string key, T document, CancellationToken cancellationToken = default);
    Task<T> Get(string key, CancellationToken cancellationToken = default);
    Task Delete(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}
```

### Benefits of Generic API

- **Type Safety**: Compile-time type checking
- **IntelliSense**: Better IDE support
- **Less Verbosity**: No need to specify `<T>` on every method call

### Example Comparison

**Non-Generic API:**
```csharp
var users = db.Collection("users");
await users.Put("user1", new User { Name = "Alice" });
var user = await users.Get<User>("user1");
var results = await users.Find<User>(u => u.Age > 30);
```

**Generic API:**
```csharp
var users = db.Collection<User>();
await users.Put("user1", new User { Name = "Alice" });
var user = await users.Get("user1"); // Type inferred!
var results = await users.Find(u => u.Age > 30); // Cleaner!
```

## Primary Keys and Indexes

### Attributes

```csharp
using EntglDb.Core.Metadata;

public class User
{
    [PrimaryKey(AutoGenerate = true)]
    public string Id { get; set; } = "";
    
    public string Name { get; set; }
    
    [Indexed]
    public int Age { get; set; }
}
```

### Primary Key Detection

EntglDb uses the following strategy to detect primary keys:

1. **Attribute**: Property marked with `[PrimaryKey]`
2. **Convention**: Property named `Id` or `{TypeName}Id`

### Auto-Generation

When `AutoGenerate = true` (default), EntglDb will automatically generate a GUID if the key is empty:

```csharp
var users = db.Collection<User>();

// Auto-generates Id
var user = new User { Name = "Alice", Age = 30 };
await users.Put(user);
Console.WriteLine(user.Id); // "3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

### Keyless Put

With a primary key defined, you can use `Put(T document)` without specifying a key:

```csharp
// Auto-detects key from entity
await users.Put(new User { Name = "Bob", Age = 25 });

// Explicit key still works
await users.Put("user-123", new User { Name = "Charlie", Age = 35 });
```

### Indexed Fields

Mark properties with `[Indexed]` to indicate they should be indexed for better query performance:

```csharp
[Indexed]
public int Age { get; set; }

[Indexed(Unique = true)]
public string Email { get; set; }
```

> **Note**: Currently, indexes are metadata-only. Future versions will create actual SQLite indexed columns for improved query performance.

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
