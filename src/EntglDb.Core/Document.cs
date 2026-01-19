using System.Text.Json;

namespace EntglDb.Core;

public class Document
{
    public string Collection { get; }
    public string Key { get; }
    public JsonElement Content { get; }
    public HlcTimestamp UpdatedAt { get; }
    public bool IsDeleted { get; }

    public Document(string collection, string key, JsonElement content, HlcTimestamp updatedAt, bool isDeleted)
    {
        Collection = collection;
        Key = key;
        Content = content;
        UpdatedAt = updatedAt;
        IsDeleted = isDeleted;
    }
}
