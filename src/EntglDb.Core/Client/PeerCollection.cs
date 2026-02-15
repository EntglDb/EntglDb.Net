using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core;

/// <summary>
/// Represents a non-generic collection of documents.
/// </summary>
internal class PeerCollection : IPeerCollection
{
    private readonly string _name;
    private readonly PeerDatabase _db;

    public PeerCollection(string name, PeerDatabase db)
    {
        _name = name;
        _db = db;
    }

    public string Name => _name;

    public Task Put(string key, object document, CancellationToken cancellationToken = default)
        => _db.PutAsync(_name, key, document, cancellationToken);

    public Task PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default)
        => _db.PutManyAsync(_name, documents, cancellationToken);

    public Task<T> Get<T>(string key, CancellationToken cancellationToken = default)
        => _db.GetAsync<T>(_name, key, cancellationToken)!;

    public Task Delete(string key, CancellationToken cancellationToken = default)
         => _db.DeleteAsync(_name, key, cancellationToken);

    public Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default)
         => _db.DeleteManyAsync(_name, keys, cancellationToken);

    public Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _db.FindAsync(_name, predicate, cancellationToken: cancellationToken);

    public Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        => _db.FindAsync(_name, predicate, skip, take, orderBy, ascending, cancellationToken);

    public Task<int> Count(Expression<Func<object, bool>>? predicate, CancellationToken cancellationToken = default)
        => _db.CountAsync<object>(_name, predicate, cancellationToken);
}

/// <summary>
/// Represents a strongly-typed collection of documents.
/// </summary>
internal class PeerCollection<T> : IPeerCollection<T>
{
    private readonly string _name;
    private readonly PeerDatabase _db;

    public PeerCollection(string name, PeerDatabase db)
    {
        _name = name;
        _db = db;
    }

    public string Name => _name;

    public Task Put(string key, T document, CancellationToken cancellationToken = default)
        => _db.PutAsync(_name, key, document!, cancellationToken);

    public Task Put(T document, CancellationToken cancellationToken = default)
    {
        var getKey = Metadata.EntityMetadata<T>.GetKey;
        if (getKey == null)
            throw new InvalidOperationException($"Type {typeof(T).Name} has no primary key defined.");

        var key = getKey(document);

        if (string.IsNullOrEmpty(key) && Metadata.EntityMetadata<T>.AutoGenerateKey)
        {
            key = Guid.NewGuid().ToString();
            Metadata.EntityMetadata<T>.SetKey?.Invoke(document, key);
        }

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException($"Primary key for {typeof(T).Name} is null or empty.");

        return Put(key, document, cancellationToken);
    }

    public Task PutMany(IEnumerable<T> documents, CancellationToken cancellationToken = default)
    {
        var list = new List<KeyValuePair<string, object>>();
        var getKey = Metadata.EntityMetadata<T>.GetKey;

        foreach (var document in documents)
        {
            if (document == null) throw new ArgumentNullException(nameof(documents), "Document cannot be null");
            if (getKey == null) throw new InvalidOperationException($"Type {typeof(T).Name} has no primary key defined.");

            var key = getKey(document);

            if (string.IsNullOrEmpty(key) && Metadata.EntityMetadata<T>.AutoGenerateKey)
            {
                key = Guid.NewGuid().ToString();
                Metadata.EntityMetadata<T>.SetKey?.Invoke(document, key);
            }

            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException($"Primary key for {typeof(T).Name} is null or empty.");

            list.Add(new KeyValuePair<string, object>(key, document));
        }

        return _db.PutManyAsync(_name, list, cancellationToken);
    }

    public Task<T> Get(string key, CancellationToken cancellationToken = default)
        => _db.GetAsync<T>(_name, key, cancellationToken)!;

    public Task Delete(string key, CancellationToken cancellationToken = default)
        => _db.DeleteAsync(_name, key, cancellationToken);

    public Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => _db.DeleteManyAsync(_name, keys, cancellationToken);

    public Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _db.FindAsync(_name, predicate, cancellationToken: cancellationToken);

    public Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        => _db.FindAsync(_name, predicate, skip, take, orderBy, ascending, cancellationToken);

    public Task<int> Count(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => _db.CountAsync(_name, predicate, cancellationToken);

    Task IPeerCollection.Put(string key, object document, CancellationToken cancellationToken)
        => _db.PutAsync(_name, key, document, cancellationToken);

    Task IPeerCollection.PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken)
        => _db.PutManyAsync(_name, documents, cancellationToken);

    Task<TResult> IPeerCollection.Get<TResult>(string key, CancellationToken cancellationToken)
        => _db.GetAsync<TResult>(_name, key, cancellationToken)!;

    Task IPeerCollection.Delete(string key, CancellationToken cancellationToken)
         => _db.DeleteAsync(_name, key, cancellationToken);

    Task IPeerCollection.DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken)
         => _db.DeleteManyAsync(_name, keys, cancellationToken);

    Task<IEnumerable<TResult>> IPeerCollection.Find<TResult>(Expression<Func<TResult, bool>> predicate, CancellationToken cancellationToken)
        => _db.FindAsync(_name, predicate, cancellationToken: cancellationToken);

    Task<IEnumerable<TResult>> IPeerCollection.Find<TResult>(Expression<Func<TResult, bool>> predicate, int? skip, int? take, string? orderBy, bool ascending, CancellationToken cancellationToken)
        => _db.FindAsync(_name, predicate, skip, take, orderBy, ascending, cancellationToken);

    Task<int> IPeerCollection.Count(Expression<Func<object, bool>>? predicate, CancellationToken cancellationToken)
        => _db.CountAsync<object>(_name, predicate, cancellationToken);
}
