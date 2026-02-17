using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Storage.Events;
using EntglDb.Core.Sync;
using BLite.Core.Collections;
using BLite.Core.CDC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

// Alias to avoid ambiguity
using BLiteOperationType = BLite.Core.Transactions.OperationType;
using EntglOperationType = EntglDb.Core.OperationType;

namespace EntglDb.Sample.Shared;

/// <summary>
/// Document store implementation that uses SampleDbContext for BLite persistence.
/// Maps between typed entities (User, TodoList) and generic Document objects.
/// Subscribes to BLite CDC to emit standard IDocumentStore events.
/// </summary>
public class SampleDocumentStore : IDocumentStore, IDisposable
{
    private readonly SampleDbContext _context;
    private readonly ILogger<SampleDocumentStore> _logger;
    private readonly IConflictResolver _conflictResolver;
    private readonly List<IDisposable> _cdcWatchers = new();

    private const string UsersCollection = "Users";
    private const string TodoListsCollection = "TodoLists";

    public SampleDocumentStore(
        SampleDbContext context, 
        IConflictResolver conflictResolver,
        ILogger<SampleDocumentStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _logger = logger ?? NullLogger<SampleDocumentStore>.Instance;

        // Subscribe to BLite CDC for all collections
        SubscribeToCDC();
    }

    public IEnumerable<string> InterestedCollection => [UsersCollection, TodoListsCollection];

    public event EventHandler<DocumentsDeletedEventArgs>? DocumentsDeleted;
    public event EventHandler<DocumentsInsertedEventArgs>? DocumentsInserted;
    public event EventHandler<DocumentsUpdatedEventArgs>? DocumentsUpdated;

    /// <summary>
    /// Subscribes to BLite Change Data Capture for all collections.
    /// Translates CDC events into standard IDocumentStore events.
    /// </summary>
    private void SubscribeToCDC()
    {
        // Watch Users collection with payload capture enabled
        var usersObservable = _context.Users.Watch(capturePayload: true);
        var usersSubscription = usersObservable.Subscribe(new CdcObserver<string, User>(
            UsersCollection,
            user => user.Id,
            this,
            _logger));
        _cdcWatchers.Add(usersSubscription);

        // Watch TodoLists collection with payload capture enabled
        var todoListsObservable = _context.TodoLists.Watch(capturePayload: true);
        var todoListsSubscription = todoListsObservable.Subscribe(new CdcObserver<string, TodoList>(
            TodoListsCollection,
            todoList => todoList.Id,
            this,
            _logger));
        _cdcWatchers.Add(todoListsSubscription);

        _logger.LogInformation("Subscribed to BLite CDC for collections: {Collections}", 
            string.Join(", ", InterestedCollection));
    }

    /// <summary>
    /// Observer for BLite CDC events that translates to IDocumentStore events.
    /// </summary>
    private class CdcObserver<TId, TEntity> : IObserver<ChangeStreamEvent<TId, TEntity>> 
        where TEntity : class
    {
        private readonly string _collectionName;
        private readonly Func<TEntity, string> _idSelector;
        private readonly SampleDocumentStore _store;
        private readonly ILogger _logger;

        public CdcObserver(
            string collectionName,
            Func<TEntity, string> idSelector,
            SampleDocumentStore store,
            ILogger logger)
        {
            _collectionName = collectionName;
            _idSelector = idSelector;
            _store = store;
            _logger = logger;
        }

        public void OnNext(ChangeStreamEvent<TId, TEntity> changeEvent)
        {
            try
            {
                var entityId = changeEvent.DocumentId?.ToString() ?? "";
                
                // For Insert/Update, use Entity from CDC; for Delete, entity might be null
                if (changeEvent.Type == BLiteOperationType.Delete)
                {
                    _store.DocumentsDeleted?.Invoke(_store, 
                        new DocumentsDeletedEventArgs(_collectionName, [entityId]));
                    
                    _logger.LogDebug("CDC Delete: {Collection}/{Id}", _collectionName, entityId);
                }
                else if (changeEvent.Entity != null)
                {
                    var document = EntityToDocument(_collectionName, _idSelector(changeEvent.Entity), changeEvent.Entity);
                    
                    if (changeEvent.Type == BLiteOperationType.Insert)
                    {
                        _store.DocumentsInserted?.Invoke(_store, 
                            new DocumentsInsertedEventArgs(_collectionName, [document]));
                        
                        _logger.LogDebug("CDC Insert: {Collection}/{Id}", _collectionName, entityId);
                    }
                    else if (changeEvent.Type == BLiteOperationType.Update)
                    {
                        _store.DocumentsUpdated?.Invoke(_store, 
                            new DocumentsUpdatedEventArgs(_collectionName, [document]));
                        
                        _logger.LogDebug("CDC Update: {Collection}/{Id}", _collectionName, entityId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CDC event for {Collection}", _collectionName);
            }
        }

        public void OnError(Exception error)
        {
            _logger.LogError(error, "CDC stream error for collection {Collection}", _collectionName);
        }

        public void OnCompleted()
        {
            _logger.LogInformation("CDC stream completed for collection {Collection}", _collectionName);
        }
    }

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        foreach (var key in documentKeys)
        {
            // Try to find and delete from Users
            var user = _context.Users.FindById(key);
            if (user != null)
            {
                await _context.Users.DeleteAsync(key);
                continue;
            }

            // Try to find and delete from TodoLists
            var todoList = _context.TodoLists.FindById(key);
            if (todoList != null)
            {
                await _context.TodoLists.DeleteAsync(key);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        // CDC will emit events automatically
        
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
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
                _logger.LogWarning("Unknown collection: {Collection}", collection);
                return false;
        }

        await _context.SaveChangesAsync(cancellationToken);
        // CDC will emit events automatically
        
        return true;
    }

    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        // Delete all users
        var userIds = _context.Users.FindAll().Select(u => u.Id).ToList();
        await _context.Users.DeleteBulkAsync(userIds);

        // Delete all todo lists
        var todoListIds = _context.TodoLists.FindAll().Select(t => t.Id).ToList();
        await _context.TodoLists.DeleteBulkAsync(todoListIds);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Dropped all documents from SampleDocumentStore");
    }

    public Task<IEnumerable<Document>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        // Export Users
        foreach (var user in _context.Users.FindAll())
        {
            documents.Add(EntityToDocument(UsersCollection, user.Id, user));
        }

        // Export TodoLists
        foreach (var todoList in _context.TodoLists.FindAll())
        {
            documents.Add(EntityToDocument(TodoListsCollection, todoList.Id, todoList));
        }

        return Task.FromResult<IEnumerable<Document>>(documents);
    }

    public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        Document? document = collection switch
        {
            UsersCollection => GetUserDocument(key),
            TodoListsCollection => GetTodoListDocument(key),
            _ => null
        };

        return Task.FromResult(document);
    }

    public Task<IEnumerable<Document>> GetDocumentsAsync(List<(string Collection, string Key)> documentKeys, CancellationToken cancellationToken)
    {
        var documents = new List<Document>();

        foreach (var (collection, key) in documentKeys)
        {
            var doc = collection switch
            {
                UsersCollection => GetUserDocument(key),
                TodoListsCollection => GetTodoListDocument(key),
                _ => null
            };

            if (doc != null)
            {
                documents.Add(doc);
            }
        }

        return Task.FromResult<IEnumerable<Document>>(documents);
    }

    public Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        IEnumerable<Document> documents = collection switch
        {
            UsersCollection => _context.Users.FindAll().Select(u => EntityToDocument(UsersCollection, u.Id, u)),
            TodoListsCollection => _context.TodoLists.FindAll().Select(t => EntityToDocument(TodoListsCollection, t.Id, t)),
            _ => []
        };

        return Task.FromResult(documents);
    }

    public async Task ImportAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        foreach (var document in items)
        {
            if (document.IsDeleted)
            {
                await DeleteDocumentAsync(document.Collection, document.Key, cancellationToken);
            }
            else
            {
                await PutDocumentAsync(document, cancellationToken);
            }
        }
    }

    public async Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            await InsertDocumentInternal(document);
        }

        await _context.SaveChangesAsync(cancellationToken);
        // CDC will emit events automatically

        return true;
    }

    public async Task<Document> MergeAsync(Document incoming, CancellationToken cancellationToken = default)
    {
        var existing = await GetDocumentAsync(incoming.Collection, incoming.Key, cancellationToken);
        
        if (existing == null)
        {
            // No existing document, insert new one
            await PutDocumentAsync(incoming, cancellationToken);
            return incoming;
        }

        // Use conflict resolver to determine the merge result
        var oplogEntry = new OplogEntry(
            incoming.Collection,
            incoming.Key,
            incoming.IsDeleted ? EntglOperationType.Delete : EntglOperationType.Put,
            incoming.Content,
            incoming.UpdatedAt,
            "" // previousHash not needed for merge
        );

        var resolution = _conflictResolver.Resolve(existing, oplogEntry);
        
        if (resolution.ShouldApply && resolution.MergedDocument != null)
        {
            await PutDocumentAsync(resolution.MergedDocument, cancellationToken);
            return resolution.MergedDocument;
        }

        return existing;
    }

    public async Task MergeAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await MergeAsync(item, cancellationToken);
        }
    }

    public async Task<bool> PutDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                var existingUser = _context.Users.FindById(document.Key);
                var user = DocumentToEntity<User>(document);
                user.Id = document.Key;
                
                if (existingUser == null)
                {
                    await _context.Users.InsertAsync(user);
                }
                else
                {
                    await _context.Users.UpdateAsync(user);
                }
                break;

            case TodoListsCollection:
                var existingTodoList = _context.TodoLists.FindById(document.Key);
                var todoList = DocumentToEntity<TodoList>(document);
                todoList.Id = document.Key;
                
                if (existingTodoList == null)
                {
                    await _context.TodoLists.InsertAsync(todoList);
                }
                else
                {
                    await _context.TodoLists.UpdateAsync(todoList);
                }
                break;

            default:
                _logger.LogWarning("Unknown collection: {Collection}", document.Collection);
                return false;
        }

        await _context.SaveChangesAsync(cancellationToken);
        // CDC will emit events automatically

        return true;
    }

    public async Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            await UpdateDocumentInternal(document);
        }

        await _context.SaveChangesAsync(cancellationToken);
        // CDC will emit events automatically

        return true;
    }

    #region Private Helpers

    private Document? GetUserDocument(string key)
    {
        var user = _context.Users.FindById(key);
        return user != null ? EntityToDocument(UsersCollection, key, user) : null;
    }

    private Document? GetTodoListDocument(string key)
    {
        var todoList = _context.TodoLists.FindById(key);
        return todoList != null ? EntityToDocument(TodoListsCollection, key, todoList) : null;
    }

    private async Task<bool> InsertDocumentInternal(Document document)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                var user = DocumentToEntity<User>(document);
                user.Id = document.Key;
                await _context.Users.InsertAsync(user);
                return true;

            case TodoListsCollection:
                var todoList = DocumentToEntity<TodoList>(document);
                todoList.Id = document.Key;
                await _context.TodoLists.InsertAsync(todoList);
                return true;

            default:
                return false;
        }
    }

    private async Task<bool> UpdateDocumentInternal(Document document)
    {
        switch (document.Collection)
        {
            case UsersCollection:
                var user = DocumentToEntity<User>(document);
                user.Id = document.Key;
                await _context.Users.UpdateAsync(user);
                return true;

            case TodoListsCollection:
                var todoList = DocumentToEntity<TodoList>(document);
                todoList.Id = document.Key;
                await _context.TodoLists.UpdateAsync(todoList);
                return true;

            default:
                return false;
        }
    }

    private static Document EntityToDocument<T>(string collection, string key, T entity)
    {
        var json = JsonSerializer.Serialize(entity);
        var content = JsonSerializer.Deserialize<JsonElement>(json);
        var defaultTimestamp = new HlcTimestamp(0, 0, "");
        return new Document(collection, key, content, defaultTimestamp, false);
    }

    private static T DocumentToEntity<T>(Document document) where T : new()
    {
        return JsonSerializer.Deserialize<T>(document.Content.GetRawText()) ?? new T();
    }

    #endregion

    public void Dispose()
    {
        // Unsubscribe from all CDC watchers
        foreach (var watcher in _cdcWatchers)
        {
            try
            {
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing CDC watcher");
            }
        }
        _cdcWatchers.Clear();
        
        _logger.LogInformation("SampleDocumentStore disposed - CDC watchers cleaned up");
    }
}
