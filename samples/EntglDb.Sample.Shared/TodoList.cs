using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EntglDb.Sample.Shared;

public class TodoList
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<TodoItem> Items { get; set; } = new();
}

public class TodoItem
{
    public string Task { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
