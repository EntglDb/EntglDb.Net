using System;
using System.Collections.Generic;

namespace EntglDb.Core.Storage.Events;

public class DocumentsUpdatedEventArgs : EventArgs
{
    public string Collection { get; }
    public IEnumerable<Document> Documents { get; }
    public DocumentsUpdatedEventArgs(string collection, IEnumerable<Document> documents)
    {
        Collection = collection;
        Documents = documents;
    }
}
