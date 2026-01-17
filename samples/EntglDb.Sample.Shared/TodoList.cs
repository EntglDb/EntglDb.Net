using System.Collections.Generic;

namespace EntglDb.Sample.Shared;

public class TodoList
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<TodoItem> Items { get; set; } = new();
}

public class TodoItem
{
    public string id { get; set; } = Guid.NewGuid().ToString(); // lowercase for merge strategy
    public string Task { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
