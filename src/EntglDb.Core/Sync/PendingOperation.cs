using System;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Sync;

/// <summary>
/// Represents a pending operation to be executed when connection is restored.
/// </summary>
public class PendingOperation
{
    public string Type { get; set; } = "";
    public string Collection { get; set; } = "";
    public string Key { get; set; } = "";
    public object? Data { get; set; }
    public DateTime QueuedAt { get; set; }
}
