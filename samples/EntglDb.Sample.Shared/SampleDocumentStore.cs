using EntglDb.Core;
using EntglDb.Core.Network;
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
        IPeerNodeConfigurationProvider configProvider,
        ILogger<SampleDocumentStore>? logger = null)
        : base(context, configProvider, new LastWriteWinsConflictResolver(), logger)
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
        switch (collection)
        {
            case UsersCollection:
                var user = content.Deserialize<User>()!;
                user.Id = key;
                
                var existingUser = _context.Users.Find(u => u.Id == key).FirstOrDefault();
                if (existingUser != null)
                {
                    await _context.Users.UpdateAsync(user);
                }
                else
                {
                    await _context.Users.InsertAsync(user);
                }
                await _context.SaveChangesAsync(cancellationToken);
                break;

            case TodoListsCollection:
                var todoList = content.Deserialize<TodoList>()!;
                todoList.Id = key;
                
                var existingTodoList = _context.TodoLists.Find(t => t.Id == key).FirstOrDefault();
                if (existingTodoList != null)
                {
                    await _context.TodoLists.UpdateAsync(todoList);
                }
                else
                {
                    await _context.TodoLists.InsertAsync(todoList);
                }
                await _context.SaveChangesAsync(cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Collection '{collection}' is not supported for sync.");
        }
    }

    protected override Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        return Task.FromResult<JsonElement?>(collection switch
        {
            UsersCollection => SerializeEntity(_context.Users.Find(u => u.Id == key).FirstOrDefault()),
            TodoListsCollection => SerializeEntity(_context.TodoLists.Find(t => t.Id == key).FirstOrDefault()),
            _ => null
        });
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        switch (collection)
        {
            case UsersCollection:
                var users = _context.Users.Find(u => u.Id == key).ToList();
                if (users.Any())
                {
                    await _context.Users.DeleteBulkAsync(users.Select(u => u.Id).ToList());
                    await _context.SaveChangesAsync(cancellationToken);
                }
                break;

            case TodoListsCollection:
                var todoLists = _context.TodoLists.Find(t => t.Id == key).ToList();
                if (todoLists.Any())
                {
                    await _context.TodoLists.DeleteBulkAsync(todoLists.Select(t => t.Id).ToList());
                    await _context.SaveChangesAsync(cancellationToken);
                }
                break;

            default:
                _logger.LogWarning("Attempted to remove entity from unsupported collection: {Collection}", collection);
                break;
        }
    }

    protected override async Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken cancellationToken)
    {
        return await Task.Run(() => collection switch
        {
            UsersCollection => _context.Users.FindAll()
                .Select(u => (u.Id, SerializeEntity(u)!.Value)),

            TodoListsCollection => _context.TodoLists.FindAll()
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
