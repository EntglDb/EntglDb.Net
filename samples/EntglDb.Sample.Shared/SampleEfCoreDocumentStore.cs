using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Storage.Events;
using EntglDb.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace EntglDb.Sample.Shared;

/// <summary>
/// EF Core DocumentStore implementation for Sample application.
/// Uses EF Core ChangeTracking to automatically emit events.
/// </summary>
public class SampleEfCoreDocumentStore : IDocumentStore
{
    private readonly SampleEfCoreDbContext _context;
    private readonly ILogger<SampleEfCoreDocumentStore> _logger;
    private readonly IConflictResolver _conflictResolver;

    private const string UsersCollection = "Users";
    private const string TodoListsCollection = "TodoLists";

    public SampleEfCoreDocumentStore(
        SampleEfCoreDbContext context,
        IConflictResolver conflictResolver,
        ILogger<SampleEfCoreDocumentStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _logger = logger ?? NullLogger<SampleEfCoreDocumentStore>.Instance;
    }

    public IEnumerable<string> InterestedCollection => [UsersCollection, TodoListsCollection];

    public event EventHandler<DocumentsDeletedEventArgs>? DocumentsDeleted;
    public event EventHandler<DocumentsInsertedEventArgs>? DocumentsInserted;
    public event EventHandler<DocumentsUpdatedEventArgs>? DocumentsUpdated;

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        foreach (var key in documentKeys)
        {
            var user = await _context.Users.FindAsync([key], cancellationToken);
            if (user != null)
            {
                _context.Users.Remove(user);
                continue;
            }

            var todoList = await _context.TodoLists.FindAsync([key], cancellationToken);
            if (todoList != null)
            {
                _context.TodoLists.Remove(todoList);
            }
        }

        await SaveChangesAndEmitEventsAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        switch (collection)
        {
            case UsersCollection:
                var user = await _context.Users.FindAsync([key], cancellationToken);
                if (user != null) _context.Users.Remove(user);
                break;
            case TodoListsCollection:
                var todoList = await _context.TodoLists.FindAsync([key], cancellationToken);
                if (todoList != null) _context.TodoLists.Remove(todoList);
                break;
            default:
                return false;
        }

        await SaveChangesAndEmitEventsAsync(cancellationToken);
        return true;
    }

    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        _context.Users.RemoveRange(_context.Users);
        _context.TodoLists.RemoveRange(_context.TodoLists);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Document>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        var users = await _context.Users.ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            documents.Add(EntityToDocument(UsersCollection, user.Id, user));
        }

        var todoLists = await _context.TodoLists.ToListAsync(cancellationToken);
        foreach (var todoList in todoLists)
        {
            documents.Add(EntityToDocument(TodoListsCollection, todoList.Id, todoList));
        }

        return documents;
    }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        switch (collection)
        {
            case UsersCollection:
                var user = await _context.Users.FindAsync([key], cancellationToken);
                return user != null ? EntityToDocument(UsersCollection, user.Id, user) : null;

            case TodoListsCollection:
                var todoList = await _context.TodoLists.FindAsync([key], cancellationToken);
                return todoList != null ? EntityToDocument(TodoListsCollection, todoList.Id, todoList) : null;

            default:
                return null;
        }
    }

    public async Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        switch (collection)
        {
            case UsersCollection:
                var users = await _context.Users.ToListAsync(cancellationToken);
                documents.AddRange(users.Select(u => EntityToDocument(UsersCollection, u.Id, u)));
                break;
            case TodoListsCollection:
                var todoLists = await _context.TodoLists.ToListAsync(cancellationToken);
                documents.AddRange(todoLists.Select(t => EntityToDocument(TodoListsCollection, t.Id, t)));
                break;
        }

        return documents;
    }

    public async Task<IEnumerable<Document>> GetDocumentsAsync(List<(string Collection, string Key)> documentIds, CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        foreach (var (collection, key) in documentIds)
        {
            var doc = await GetDocumentAsync(collection, key, cancellationToken);
            if (doc != null)
                documents.Add(doc);
        }

        return documents;
    }

    public async Task ImportAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            InsertDocumentInternal(doc);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            InsertDocumentInternal(doc);
        }

        await SaveChangesAndEmitEventsAsync(cancellationToken);
        return true;
    }

    public async Task<Document> MergeAsync(Document incoming, CancellationToken cancellationToken = default)
    {
        var existing = await GetDocumentAsync(incoming.Collection, incoming.Key, cancellationToken);

        if (existing == null)
        {
            await PutDocumentAsync(incoming, cancellationToken);
            return incoming;
        }

        var oplogEntry = new OplogEntry(
            incoming.Collection,
            incoming.Key,
            incoming.IsDeleted ? OperationType.Delete : OperationType.Put,
            incoming.Content,
            incoming.UpdatedAt,
            ""
        );

        var resolution = _conflictResolver.Resolve(existing, oplogEntry);

        if (resolution.ShouldApply && resolution.MergedDocument != null)
        {
            await PutDocumentAsync(resolution.MergedDocument, cancellationToken);
            return resolution.MergedDocument;
        }

        return existing;
    }

    public async Task MergeAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            await MergeAsync(doc, cancellationToken);
        }
    }

    public async Task<bool> PutDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                var user = DocumentToEntity<User>(document);
                var existingUser = await _context.Users.FindAsync([document.Key], cancellationToken);
                if (existingUser == null)
                    _context.Users.Add(user);
                else
                    _context.Entry(existingUser).CurrentValues.SetValues(user);
                break;

            case TodoListsCollection:
                var todoList = DocumentToEntity<TodoList>(document);
                var existingTodo = await _context.TodoLists.FindAsync([document.Key], cancellationToken);
                if (existingTodo == null)
                    _context.TodoLists.Add(todoList);
                else
                    _context.Entry(existingTodo).CurrentValues.SetValues(todoList);
                break;

            default:
                return false;
        }

        await SaveChangesAndEmitEventsAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            await UpdateDocumentInternal(doc);
        }

        await SaveChangesAndEmitEventsAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Saves changes and emits events based on EF Core ChangeTracking.
    /// </summary>
    private async Task SaveChangesAndEmitEventsAsync(CancellationToken cancellationToken)
    {
        var entries = _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        var changedByCollection = new Dictionary<string, (List<Document> inserted, List<Document> updated, List<string> deleted)>();

        foreach (var entry in entries)
        {
            string? collection = null;
            string? key = null;
            object? entity = null;

            if (entry.Entity is User user)
            {
                collection = UsersCollection;
                key = user.Id;
                entity = user;
            }
            else if (entry.Entity is TodoList todoList)
            {
                collection = TodoListsCollection;
                key = todoList.Id;
                entity = todoList;
            }

            if (collection == null || key == null) continue;

            if (!changedByCollection.ContainsKey(collection))
                changedByCollection[collection] = (new(), new(), new());

            switch (entry.State)
            {
                case EntityState.Added:
                    changedByCollection[collection].inserted.Add(EntityToDocument(collection, key, entity!));
                    break;
                case EntityState.Modified:
                    changedByCollection[collection].updated.Add(EntityToDocument(collection, key, entity!));
                    break;
                case EntityState.Deleted:
                    changedByCollection[collection].deleted.Add(key);
                    break;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Emit events after successful save, grouped by collection
        foreach (var (collection, (inserted, updated, deleted)) in changedByCollection)
        {
            if (inserted.Count > 0)
                DocumentsInserted?.Invoke(this, new DocumentsInsertedEventArgs(collection, inserted));

            if (updated.Count > 0)
                DocumentsUpdated?.Invoke(this, new DocumentsUpdatedEventArgs(collection, updated));

            if (deleted.Count > 0)
                DocumentsDeleted?.Invoke(this, new DocumentsDeletedEventArgs(collection, deleted));
        }
    }

    private void InsertDocumentInternal(Document document)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                _context.Users.Add(DocumentToEntity<User>(document));
                break;
            case TodoListsCollection:
                _context.TodoLists.Add(DocumentToEntity<TodoList>(document));
                break;
        }
    }

    private async Task<bool> UpdateDocumentInternal(Document document)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                var user = DocumentToEntity<User>(document);
                var existingUser = await _context.Users.FindAsync(document.Key);
                if (existingUser != null)
                    _context.Entry(existingUser).CurrentValues.SetValues(user);
                break;
            case TodoListsCollection:
                var todoList = DocumentToEntity<TodoList>(document);
                var existingTodo = await _context.TodoLists.FindAsync(document.Key);
                if (existingTodo != null)
                    _context.Entry(existingTodo).CurrentValues.SetValues(todoList);
                break;
            default:
                return false;
        }
        return true;
    }

    private static Document EntityToDocument<T>(string collection, string key, T entity) where T : class
    {
        var json = JsonSerializer.Serialize(entity);
        var content = JsonDocument.Parse(json).RootElement;
        return new Document(collection, key, content, new HlcTimestamp(0, 0, ""), false);
    }

    private static T DocumentToEntity<T>(Document document) where T : class
    {
        var json = document.Content.GetRawText();
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Failed to deserialize document");
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
