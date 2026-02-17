using EntglDb.Core.Sync;
using System;
using System.Text.Json;

namespace EntglDb.Core;

public class Document
{
    public string Collection { get; private set; }
    public string Key { get; private set; }
    public JsonElement Content { get; private set; }
    public HlcTimestamp UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public Document(string collection, string key, JsonElement content, HlcTimestamp updatedAt, bool isDeleted)
    {
        Collection = collection;
        Key = key;
        Content = content;
        UpdatedAt = updatedAt;
        IsDeleted = isDeleted;
    }

    public void Merge(OplogEntry oplogEntry, IConflictResolver? resolver = null)
    {
        if (oplogEntry == null) return;
        if (Collection != oplogEntry.Collection) return;
        if (Key != oplogEntry.Key) return;
        if (resolver == null)
        {
            //last wins
            if(UpdatedAt <= oplogEntry.Timestamp)
            {
                Content = oplogEntry.Payload ?? default;
                UpdatedAt = oplogEntry.Timestamp;
                IsDeleted = oplogEntry.Operation == OperationType.Delete;
            }
            return;
        }
        var resolutionResult = resolver.Resolve(this, oplogEntry);
        if (resolutionResult.ShouldApply && resolutionResult.MergedDocument != null)
        {
            Content = resolutionResult.MergedDocument.Content;
            UpdatedAt = resolutionResult.MergedDocument.UpdatedAt;
            IsDeleted = resolutionResult.MergedDocument.IsDeleted;
        }
    }
}
