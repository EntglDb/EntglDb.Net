using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EntglDb.Sample.Shared;

/// <summary>
/// Document store implementation for EntglDb Sample using BLite persistence.
/// Extends BLiteDocumentStore to automatically handle Oplog creation via CDC.
/// </summary>
public class SampleDocumentStore : BLiteDocumentStore<SampleDbContext>
{
    private const string UsersCollection = "Users";
    private const string TodoListsCollection = "TodoLists";

    public SampleDocumentStore(
        SampleDbContext context,
        EntglDbMetaContext metaContext,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        IPendingChangesService pendingChangesService,
        ILogger<SampleDocumentStore>? logger = null)
        : base(context, metaContext, configProvider, vectorClockService, pendingChangesService, new LastWriteWinsConflictResolver(), logger)
    {
        // Register CDC watchers for local change detection
        // InterestedCollection is automatically populated
        WatchCollection(UsersCollection, context.Users, u => u.Id);
        WatchCollection(TodoListsCollection, context.TodoLists, t => t.Id);
    }

    #region Abstract Method Implementations

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken)
    {
        await UpsertEntity(collection, key, content);
        await _context.SaveChangesAsync(cancellationToken);
    }

    protected override async Task ApplyContentToEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key, JsonElement Content)> documents, CancellationToken cancellationToken)
    {
        foreach (var (collection, key, content) in documents)
        {
            await UpsertEntity(collection, key, content);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertEntity(string collection, string key, JsonElement content)
    {
        switch (collection)
        {
            case UsersCollection:
                var user = content.Deserialize<User>()!;
                user.Id = key;
                var existingUser = await _context.Users.FindAsync(u => u.Id == key).FirstOrDefaultAsync();
                if (existingUser != null)
                    await _context.Users.UpdateAsync(user);
                else
                    await _context.Users.InsertAsync(user);
                break;

            case TodoListsCollection:
                var todoList = content.Deserialize<TodoList>()!;
                todoList.Id = key;
                var existingTodoList = await _context.TodoLists.FindAsync(t => t.Id == key).FirstOrDefaultAsync();
                if (existingTodoList != null)
                    await _context.TodoLists.UpdateAsync(todoList);
                else
                    await _context.TodoLists.InsertAsync(todoList);
                break;

            default:
                throw new NotSupportedException($"Collection '{collection}' is not supported for sync.");
        }
    }

    protected override async Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        return collection switch
        {
            UsersCollection => SerializeEntity(await _context.Users.FindAsync(u => u.Id == key).FirstOrDefaultAsync()),
            TodoListsCollection => SerializeEntity(await _context.TodoLists.FindAsync(t => t.Id == key).FirstOrDefaultAsync()),
            _ => null
        };
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        await DeleteEntity(collection, key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    protected override async Task RemoveEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key)> documents, CancellationToken cancellationToken)
    {
        foreach (var (collection, key) in documents)
        {
            await DeleteEntity(collection, key);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteEntity(string collection, string key)
    {
        switch (collection)
        {
            case UsersCollection:
                await _context.Users.DeleteAsync(key);
                break;
            case TodoListsCollection:
                await _context.TodoLists.DeleteAsync(key);
                break;
            default:
                _logger.LogWarning("Attempted to remove entity from unsupported collection: {Collection}", collection);
                break;
        }
    }

    protected override async Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken cancellationToken)
    {
        return await Task.Run(async () => collection switch
        {
            UsersCollection => (await _context.Users.FindAllAsync().ToListAsync())
                .Select(u => (u.Id, SerializeEntity(u)!.Value)),

            TodoListsCollection => (await _context.TodoLists.FindAllAsync().ToListAsync())
                .Select(t => (t.Id, SerializeEntity(t)!.Value)),

            _ => Enumerable.Empty<(string, JsonElement)>()
        }, cancellationToken);
    }

    #endregion

    #region Helper Methods

    private static JsonElement? SerializeEntity<T>(T? entity) where T : class
    {
        if (entity == null) return null;
        return JsonSerializer.SerializeToElement(entity);
    }

    #endregion
}
