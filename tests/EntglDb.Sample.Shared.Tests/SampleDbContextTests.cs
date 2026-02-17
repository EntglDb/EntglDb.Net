using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace EntglDb.Sample.Shared.Tests;

public class SampleDbContextTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SampleDbContext _context;

    public SampleDbContextTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_sample_{Guid.NewGuid()}.db");
        _context = new SampleDbContext(_dbPath);
    }

    public void Dispose()
    {
        _context?.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void Context_ShouldInitializeCollections()
    {
        // Verifica che le collezioni siano state inizializzate
        _context.Should().NotBeNull();
        _context.Users.Should().NotBeNull("Users collection should be initialized by BLite");
        _context.TodoLists.Should().NotBeNull("TodoLists collection should be initialized by BLite");
        _context.OplogEntries.Should().NotBeNull("OplogEntries collection should be initialized by BLite");
    }

    [Fact]
    public async Task Users_Insert_ShouldPersist()
    {
        // Arrange
        var user = new User
        {
            Id = "user1",
            Name = "Alice",
            Age = 30,
            Address = new Address { City = "Rome" }
        };

        // Act
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = _context.Users.FindById("user1");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Alice");
        retrieved.Age.Should().Be(30);
        retrieved.Address?.City.Should().Be("Rome");
    }

    [Fact]
    public async Task Users_Update_ShouldModifyExisting()
    {
        // Arrange
        var user = new User { Id = "user2", Name = "Bob", Age = 25 };
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();

        // Act
        user.Age = 26;
        user.Address = new Address { City = "Milan" };
        await _context.Users.UpdateAsync(user);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = _context.Users.FindById("user2");
        retrieved.Should().NotBeNull();
        retrieved!.Age.Should().Be(26);
        retrieved.Address?.City.Should().Be("Milan");
    }

    [Fact]
    public async Task Users_Delete_ShouldRemove()
    {
        // Arrange
        var user = new User { Id = "user3", Name = "Charlie", Age = 35 };
        await _context.Users.InsertAsync(user);
        await _context.SaveChangesAsync();

        // Act
        await _context.Users.DeleteAsync("user3");
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = _context.Users.FindById("user3");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task TodoLists_InsertWithItems_ShouldPersist()
    {
        // Arrange
        var todoList = new TodoList
        {
            Id = "list1",
            Name = "Shopping",
            Items = new List<TodoItem>
            {
                new() { Task = "Buy milk", Completed = false },
                new() { Task = "Buy bread", Completed = true }
            }
        };

        // Act
        await _context.TodoLists.InsertAsync(todoList);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = _context.TodoLists.FindById("list1");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Shopping");
        retrieved.Items.Should().HaveCount(2);
        retrieved.Items.Should().Contain(i => i.Task == "Buy milk" && !i.Completed);
        retrieved.Items.Should().Contain(i => i.Task == "Buy bread" && i.Completed);
    }

    [Fact]
    public async Task TodoLists_UpdateItems_ShouldModifyNestedCollection()
    {
        // Arrange
        var todoList = new TodoList
        {
            Id = "list2",
            Name = "Work Tasks",
            Items = new List<TodoItem>
            {
                new() { Task = "Write report", Completed = false }
            }
        };
        await _context.TodoLists.InsertAsync(todoList);
        await _context.SaveChangesAsync();

        // Act - Mark task as completed and add new task
        todoList.Items[0].Completed = true;
        todoList.Items.Add(new TodoItem { Task = "Review report", Completed = false });
        await _context.TodoLists.UpdateAsync(todoList);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = _context.TodoLists.FindById("list2");
        retrieved.Should().NotBeNull();
        retrieved!.Items.Should().HaveCount(2);
        retrieved.Items.First().Completed.Should().Be(true);
        retrieved.Items.Last().Completed.Should().Be(false);
    }

    [Fact]
    public void Users_FindAll_ShouldReturnAllUsers()
    {
        // Arrange
        _context.Users.InsertAsync(new User { Id = "u1", Name = "User1", Age = 20 }).Wait();
        _context.Users.InsertAsync(new User { Id = "u2", Name = "User2", Age = 30 }).Wait();
        _context.Users.InsertAsync(new User { Id = "u3", Name = "User3", Age = 40 }).Wait();
        _context.SaveChangesAsync().Wait();

        // Act
        var allUsers = _context.Users.FindAll().ToList();

        // Assert
        allUsers.Should().HaveCount(3);
        allUsers.Select(u => u.Name).Should().Contain(new[] { "User1", "User2", "User3" });
    }

    [Fact]
    public void Users_Find_WithPredicate_ShouldFilterCorrectly()
    {
        // Arrange
        _context.Users.InsertAsync(new User { Id = "f1", Name = "Young", Age = 18 }).Wait();
        _context.Users.InsertAsync(new User { Id = "f2", Name = "Adult", Age = 30 }).Wait();
        _context.Users.InsertAsync(new User { Id = "f3", Name = "Senior", Age = 65 }).Wait();
        _context.SaveChangesAsync().Wait();

        // Act
        var adults = _context.Users.Find(u => u.Age >= 30).ToList();

        // Assert
        adults.Should().HaveCount(2);
        adults.Select(u => u.Name).Should().Contain(new[] { "Adult", "Senior" });
    }
}
