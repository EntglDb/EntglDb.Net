using System;
using System.Collections.Generic;

namespace EntglDb.Core.Storage.Events;

/// <summary>
/// Provides data for an event that occurs when one or more documents are deleted from a collection.
/// </summary>
public class DocumentsDeletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the name of the collection associated with this instance.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the collection of keys that identify the available documents.
    /// </summary>
    public IEnumerable<string> DocumentKeys { get; }

    /// <summary>
    /// Initializes a new instance of the DocumentsDeletedEventArgs class with the specified collection name and
    /// document keys.
    /// </summary>
    /// <param name="collection">The name of the collection from which documents were deleted. Cannot be null or empty.</param>
    /// <param name="documentKeys">A collection of keys identifying the documents that were deleted. Cannot be null.</param>
    public DocumentsDeletedEventArgs(string collection, IEnumerable<string> documentKeys)
    {
        Collection = collection;
        DocumentKeys = documentKeys;
    }
}
