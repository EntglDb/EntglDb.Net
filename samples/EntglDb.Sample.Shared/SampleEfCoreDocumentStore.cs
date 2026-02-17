using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace EntglDb.Sample.Shared;

/// <summary>
/// EF Core DocumentStore implementation for EntglDb Sample.
/// Extends EfCoreDocumentStore to automatically handle Oplog creation.
/// Maps between typed entities (User, TodoList) and generic Document objects.
/// </summary>
public class SampleEfCoreDocumentStore : EfCoreDocumentStore<SampleEfCoreDbContext>
{
    private const string UsersCollection = "Users";
    private const string TodoListsCollection = "TodoLists";

    public SampleEfCoreDocumentStore(
        SampleEfCoreDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        ILogger<SampleEfCoreDocumentStore>? logger = null)
        : base(context, configProvider, new LastWriteWinsConflictResolver(), logger)
    {
    }

    public override IEnumerable<string> InterestedCollection => new[] { UsersCollection, TodoListsCollection };

    #region Abstract Method Implementations

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken)
    {
        switch (collection)
        {
            case UsersCollection:
                var user = content.Deserialize<User>()!;
                user.Id = key;

                var existingUser = await _context.Users.FindAsync([key], cancellationToken);
                if (existingUser != null)
                {
                    _context.Entry(existingUser).CurrentValues.SetValues(user);
                }
                else
                {
                    _context.Users.Add(user);
                }
                await _context.SaveChangesAsync(cancellationToken);
                break;

            case TodoListsCollection:
                var todoList = content.Deserialize<TodoList>()!;
                todoList.Id = key;

                var existingTodoList = await _context.TodoLists.FindAsync([key], cancellationToken);
                if (existingTodoList != null)
                {
                    _context.Entry(existingTodoList).CurrentValues.SetValues(todoList);
                }
                else
                {
                    _context.TodoLists.Add(todoList);
                }
                await _context.SaveChangesAsync(cancellationToken);
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
            UsersCollection => SerializeEntity(await _context.Users.FindAsync([key], cancellationToken)),
            TodoListsCollection => SerializeEntity(await _context.TodoLists.FindAsync([key], cancellationToken)),
            _ => null
        };
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        switch (collection)
        {
            case UsersCollection:
                var user = await _context.Users.FindAsync([key], cancellationToken);
                if (user != null)
                {
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                break;

            case TodoListsCollection:
                var todoList = await _context.TodoLists.FindAsync([key], cancellationToken);
                if (todoList != null)
                {
                    _context.TodoLists.Remove(todoList);
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
        return collection switch
        {
            UsersCollection => (await _context.Users.ToListAsync(cancellationToken))
                .Select(u => (u.Id, SerializeEntity(u)!.Value)),

            TodoListsCollection => (await _context.TodoLists.ToListAsync(cancellationToken))
                .Select(t => (t.Id, SerializeEntity(t)!.Value)),

            _ => Enumerable.Empty<(string, JsonElement)>()
        };
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
