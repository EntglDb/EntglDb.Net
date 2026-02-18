# BLiteDocumentStore - Usage Guide

## Overview

`BLiteDocumentStore<TDbContext>` is an abstract base class that simplifies creating document stores for EntglDb with BLite persistence. It handles all Oplog management internally, so you only need to implement entity-to-JSON mapping methods.

## Key Features

- ? **Automatic Oplog Creation** - Local changes automatically create Oplog entries
- ? **Remote Sync Handling** - AsyncLocal flag suppresses Oplog during sync (prevents duplicates)
- ? **No CDC Events Needed** - Direct Oplog management eliminates event loops
- ? **Simple API** - Only 4 abstract methods to implement

## Architecture

```
User Code ? SampleDocumentStore (extends BLiteDocumentStore)
                  ?
            BLiteDocumentStore
                  ??? _context.Users / TodoLists (read/write entities)
                  ??? _context.OplogEntries (write oplog directly)

Remote Sync ? OplogStore.ApplyBatchAsync()
                  ?
            BLiteDocumentStore.PutDocumentAsync(fromSync=true)
                  ??? _context.Users / TodoLists (write only)
                  ??? _context.OplogEntries (skip - already exists)
```

**Key Advantage**: No circular dependency! `BLiteDocumentStore` writes directly to `EntglDocumentDbContext.OplogEntries` collection.

## Implementation Example

```csharp
public class SampleDocumentStore : BLiteDocumentStore<SampleDbContext>
{
    public SampleDocumentStore(
        SampleDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        ILogger<SampleDocumentStore>? logger = null)
        : base(context, configProvider, new LastWriteWinsConflictResolver(), logger)
    {
    }

    public override IEnumerable<string> InterestedCollection => new[] { "Users", "TodoLists" };

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken ct)
    {
        switch (collection)
        {
            case "Users":
                var user = content.Deserialize<User>()!;
                user.Id = key;
                var existingUser = _context.Users.FindById(key);
                if (existingUser != null)
                    await _context.Users.UpdateAsync(user);
                else
                    await _context.Users.InsertAsync(user);
                await _context.SaveChangesAsync(ct);
                break;

            case "TodoLists":
                var todoList = content.Deserialize<TodoList>()!;
                todoList.Id = key;
                var existingTodoList = _context.TodoLists.FindById(key);
                if (existingTodoList != null)
                    await _context.TodoLists.UpdateAsync(todoList);
                else
                    await _context.TodoLists.InsertAsync(todoList);
                await _context.SaveChangesAsync(ct);
                break;

            default:
                throw new NotSupportedException($"Collection '{collection}' is not supported");
        }
    }

    protected override Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken ct)
    {
        return Task.FromResult<JsonElement?>(collection switch
        {
            "Users" => SerializeEntity(_context.Users.FindById(key)),
            "TodoLists" => SerializeEntity(_context.TodoLists.FindById(key)),
            _ => null
        });
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken ct)
    {
        switch (collection)
        {
            case "Users":
                await _context.Users.DeleteAsync(key);
                await _context.SaveChangesAsync(ct);
                break;

            case "TodoLists":
                await _context.TodoLists.DeleteAsync(key);
                await _context.SaveChangesAsync(ct);
                break;
        }
    }

    protected override async Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken ct)
    {
        return await Task.Run(() => collection switch
        {
            "Users" => _context.Users.FindAll()
                .Select(u => (u.Id, SerializeEntity(u)!.Value)),

            "TodoLists" => _context.TodoLists.FindAll()
                .Select(t => (t.Id, SerializeEntity(t)!.Value)),

            _ => Enumerable.Empty<(string, JsonElement)>()
        }, ct);
    }

    private static JsonElement? SerializeEntity<T>(T? entity) where T : class
    {
        if (entity == null) return null;
        return JsonSerializer.SerializeToElement(entity);
    }
}
```

## Usage in Application

### Setup (DI Container)

```csharp
services.AddSingleton<SampleDbContext>(sp => 
    new SampleDbContext("data/sample.blite"));

// No OplogStore dependency needed!
services.AddSingleton<IDocumentStore, SampleDocumentStore>();
services.AddSingleton<IOplogStore, BLiteOplogStore<SampleDbContext>>();
```

### Local Changes (User operations)

```csharp
// User inserts a new user
var user = new User { Id = "user-1", Name = "Alice" };
await _context.Users.InsertAsync(user);
await _context.SaveChangesAsync();

// The application then needs to notify the DocumentStore:
var document = new Document(
    "Users", 
    "user-1", 
    JsonSerializer.SerializeToElement(user),
    new HlcTimestamp(0, 0, ""),
    false);

await documentStore.PutDocumentAsync(document);
// ? This creates an OplogEntry automatically
```

### Remote Sync (Automatic)

```csharp
// When OplogStore.ApplyBatchAsync receives remote changes:
await oplogStore.ApplyBatchAsync(remoteEntries, cancellationToken);

// Internally, this calls:
using (documentStore.BeginRemoteSync()) // ? Suppresses Oplog creation
{
    foreach (var entry in remoteEntries)
    {
        await documentStore.PutDocumentAsync(entryAsDocument);
        // ? Writes to DB only, no Oplog duplication
    }
}
```

## Migration from Old CDC-based Approach

### Before (with CDC Events)
```csharp
// SampleDocumentStore subscribes to BLite CDC
// CDC emits events ? OplogCoordinator creates Oplog
// Problem: Remote sync also triggers CDC ? duplicate Oplog entries
```

### After (with BLiteDocumentStore)
```csharp
// Direct Oplog management in DocumentStore
// AsyncLocal flag prevents duplicates during sync
// No CDC events needed
```

## Benefits

1. **No Event Loops** - Direct control over Oplog creation
2. **Thread-Safe** - AsyncLocal handles concurrent operations
3. **Simpler** - Only 4 methods to implement vs full CDC subscription
4. **Transparent** - Oplog management is hidden from user code

## Next Steps

After implementing your DocumentStore:
1. Remove CDC subscriptions from your code
2. Remove `OplogCoordinator` from DI (no longer needed)
3. Test local operations create Oplog entries
4. Test remote sync doesn't create duplicate entries
