using System;
using System.Collections.Generic;

namespace EntglDb.Core.Storage.Events;

/// <summary>
/// Provides data for an event that occurs when one or more documents are inserted into a collection.
/// </summary>
/// <remarks>Use this class to access information about the collection and the documents that were inserted when
/// handling document insertion events. The properties are read-only and provide the name of the affected collection and
/// the inserted documents.</remarks>
public class DocumentsInsertedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the name of the collection associated with this instance.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the collection of documents associated with this instance.
    /// </summary>
    public IEnumerable<Document> Documents { get; }

    /// <summary>
    /// Initializes a new instance of the DocumentsInsertedEventArgs class with the specified collection name and
    /// documents.
    /// </summary>
    /// <param name="collection">The name of the collection into which the documents were inserted. Cannot be null or empty.</param>
    /// <param name="documents">The collection of documents that were inserted. Cannot be null.</param>
    public DocumentsInsertedEventArgs(string collection, IEnumerable<Document> documents)
    {
        Collection = collection;
        Documents = documents;
    }
}
