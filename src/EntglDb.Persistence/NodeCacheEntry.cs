using EntglDb.Core;

namespace EntglDb.Persistence.Sqlite;

public class NodeCacheEntry
{
    public HlcTimestamp Timestamp { get; set; }
    public string Hash { get; set; } = "";
}

