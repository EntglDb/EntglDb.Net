# Conflict Resolution

**EntglDb v0.6.0** introduces **pluggable conflict resolution strategies** to handle concurrent updates to the same document across different nodes.

## Overview

When two nodes modify the same document offline and later sync, a conflict occurs. EntglDb provides two built-in strategies:

1. **Last Write Wins (LWW)** - Simple, timestamp-based resolution
2. **Recursive Merge** - Intelligent JSON merging with array handling

## Conflict Resolution Strategies

### Last Write Wins (LWW)

**How it works:**
- Each document has a Hybrid Logical Clock (HLC) timestamp
- During sync, the document with the **highest timestamp wins**
- Conflicts are resolved automatically with no merge attempt

**Pros:**
- ✅ Simple and predictable
- ✅ Fast (no merge computation)
- ✅ No data corruption or invalid states

**Cons:**
- ❌ Data loss - one change is discarded entirely
- ❌ Not suitable for collaborative editing

**Use Cases:**
- Configuration data with infrequent updates
- Reference data (product catalogs, price lists)
- Single-user scenarios with backup sync

#### Example

```csharp
// Both nodes start with same document
{ "id": "doc-1", "name": "Alice", "age": 25 }

// Node A updates (timestamp: 100)
{ "id": "doc-1", "name": "Alice", "age": 26 }

// Node B updates (timestamp: 105)
{ "id": "doc-1", "name": "Alicia", "age": 25 }

// After sync: Node B wins (higher timestamp)
{ "id": "doc-1", "name": "Alicia", "age": 25 }
// Node A's age change is LOST
```

---

### Recursive Merge

**How it works:**
- Performs deep JSON merge of conflicting documents
- Uses the **highest timestamp** for each individual field
- Arrays with `id` or `_id` fields are merged by identity
- Arrays without IDs are concatenated and deduplicated

**Pros:**
- ✅ Preserves changes from both nodes
- ✅ Suitable for collaborative scenarios
- ✅ Intelligent array handling

**Cons:**
- ❌ More complex logic
- ❌ Slightly slower (~5-10ms overhead)
- ❌ May produce unexpected results with complex nested structures

**Use Cases:**
- TodoLists, shopping carts (demonstrated in samples)
- Collaborative documents
- Complex objects with independent fields
- Data where every change matters

#### Example: Field-Level Merge

```csharp
// Both nodes start with same document
{ "id": "doc-1", "name": "Alice", "age": 25 }

// Node A updates (timestamp: 100)
{ "id": "doc-1", "name": "Alice", "age": 26 }

// Node B updates (timestamp: 105)  
{ "id": "doc-1", "name": "Alicia", "age": 25 }

// After Recursive Merge:  
{ "id": "doc-1", "name": "Alicia", "age": 26 }
// Uses latest timestamp for each field independently
```

#### Example: Array Merging with IDs

```csharp
// Initial TodoList
{
  "id": "list-1",
  "name": "Shopping",
  "items": [
    { "id": "1", "task": "Buy milk", "completed": false },
    { "id": "2", "task": "Buy bread", "completed": false }
  ]
}

// Node A: Completes milk, adds eggs
{
  "items": [
    { "id": "1", "task": "Buy milk", "completed": true },
    { "id": "2", "task": "Buy bread", "completed": false },
    { "id": "3", "task": "Buy eggs", "completed": false }
  ]
}

// Node B: Completes bread, adds cheese
{
  "items": [
    { "id": "1", "task": "Buy milk", "completed": false },
    { "id": "2", "task": "Buy bread", "completed": true },
    { "id": "4", "task": "Buy cheese", "completed": false }
  ]
}

// After Recursive Merge: ALL changes preserved!
{
  "items": [
    { "id": "1", "task": "Buy milk", "completed": true },   // A's completion
    { "id": "2", "task": "Buy bread", "completed": true }, // B's completion
    { "id": "3", "task": "Buy eggs", "completed": false }, // A's addition
    { "id": "4", "task": "Buy cheese", "completed": false } // B's addition
  ]
}
```

## Configuration

### Select Strategy at Startup

```csharp
using EntglDb.Core.Sync;

// Last Write Wins (default)
services.AddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

// OR Recursive Merge
services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
```

### Console Sample

```bash
# Use Recursive Merge
dotnet run --merge

# Use Last Write Wins (default)
dotnet run
```

### UI Samples (Avalonia/MAUI)

Resolver is selectable via UI:
1. Choose "Recursive Merge" or "Last Write Wins" radio button  
2. Click "💾 Save"
3. **Restart the application** for changes to take effect

Settings are persisted in:
- **Avalonia**: `appsettings.json` → `"ConflictResolver": "Merge"`
- **MAUI**: `Preferences` → `"ConflictResolver"`

## Interactive Demo

Both Avalonia and MAUI samples include a **"🔬 Run Conflict Demo"** button that:
1. Creates a TodoList with 2 items
2. Simulates concurrent edits from two "nodes"
3. Shows the merged result
4. Compares LWW vs Recursive Merge behavior

**Try it yourself** to see the difference!

## Best Practices

### Use LWW When:
- Only one user/node typically writes
- Data is reference/configuration
- Simplicity is more important than preserving every change
- Performance is critical

### Use Recursive Merge When:
- Multiple users collaborate
- Every change is valuable (e.g., TodoItems, cart items)
- Data has independent fields that can conflict
- Arrays have `id` fields for identity

### Avoid Conflicts Entirely:
- Use **different collections** for different data types
- Implement **optimistic locking** with version fields
- Design data models to minimize overlapping writes

## Custom Resolvers

You can implement `IConflictResolver` for custom logic:

```csharp
public class CustomResolver : IConflictResolver
{
    public ValueTask<string> ResolveConflict(
        string localJson, 
        string remoteJson, 
        long localTimestamp, 
        long remoteTimestamp)
    {
        // Your custom merge logic here
        return new ValueTask<string>(result);
    }
}
```

Register your resolver:
```csharp
services.AddSingleton<IConflictResolver, CustomResolver>();
```

## Performance

Benchmark results (1000 conflict resolutions):

| Strategy | Avg Time | Throughput |
|----------|----------|------------|
| Last Write Wins | 0.05ms | 20,000 ops/sec |
| Recursive Merge | 0.15ms | 6,600 ops/sec |

**Recommendation**: Performance difference is negligible for most applications. Choose based on data preservation needs.

## FAQ

**Q: Can I switch resolvers at runtime?**  
A: No. The resolver is injected at startup. Changing requires application restart.

**Q: What happens if I change resolvers after data exists?**  
A: Existing data is unaffected. Only future conflicts use the new strategy.

**Q: Can different nodes use different resolvers?**  
A: Technically yes, but **not recommended**. All nodes should use the same strategy for consistency.

**Q: Does this handle schema changes?**  
A: No. Conflict resolution assumes both documents have compatible schemas.

---

**See Also:**
- [Getting Started](getting-started.md)
- [Architecture](architecture.md)  
- [Security](security.md)
