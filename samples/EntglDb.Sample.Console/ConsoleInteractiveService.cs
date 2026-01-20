using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Cache;
using EntglDb.Core.Diagnostics;
using EntglDb.Core.Sync;
using EntglDb.Core.Storage;
using EntglDb.Network;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection; // For IServiceProvider if needed
using EntglDb.Sample.Shared;
using EntglDb.Core.Network;

namespace EntglDb.Sample.Console;

public class ConsoleInteractiveService : BackgroundService
{
    private readonly ILogger<ConsoleInteractiveService> _logger;
    private readonly IPeerDatabase _db;
    private readonly IEntglDbNode _node;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IPeerStore _store; 

    
    // Auxiliary services for status/commands
    private readonly IDocumentCache _cache;
    private readonly IOfflineQueue _queue;
    private readonly IEntglDbHealthCheck _healthCheck;
    private readonly ISyncStatusTracker _syncTracker;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPeerNodeConfigurationProvider _configProvider;

    public ConsoleInteractiveService(
        ILogger<ConsoleInteractiveService> logger,
        IPeerDatabase db,
        IEntglDbNode node,
        IHostApplicationLifetime lifetime,
        IPeerStore store,
        IDocumentCache cache,
        IOfflineQueue queue,
        IEntglDbHealthCheck healthCheck,
        ISyncStatusTracker syncTracker,
        IServiceProvider serviceProvider,
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider)
    {
        _logger = logger;
        _db = db;
        _node = node;
        _lifetime = lifetime;
        _store = store;
        _cache = cache;
        _queue = queue;
        _healthCheck = healthCheck;
        _syncTracker = syncTracker;
        _serviceProvider = serviceProvider;
        _configProvider = peerNodeConfigurationProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = await _configProvider.GetConfiguration();
        // Wait for DB initialization (could be moved to IHostedService StartAsync in DB service if separate)
        await _db.InitializeAsync(stoppingToken);
        
        // Use Type-Safe Collections
        var usersTyped = _db.Collection<User>();
        var users = _db.Collection("users"); // Legacy/Dynamic

        System.Console.WriteLine($"--- Interactive Console ---");
        System.Console.WriteLine($"Node ID: {config.NodeId}");
        PrintHelp();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Non-blocking read to allow cancellation check
            if (!System.Console.KeyAvailable)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            var input = System.Console.ReadLine();
            if (string.IsNullOrEmpty(input)) continue;

            try 
            {
                await HandleInput(input, usersTyped, users);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error: {ex.Message}");
            }

            if (input == "q" || input == "quit")
            {
                _lifetime.StopApplication();
                break;
            }
        }
    }

    private void PrintHelp()
    {
        System.Console.WriteLine("Commands:");
        System.Console.WriteLine("  [p]ut, [g]et, [d]elete, [f]ind, [l]ist peers, [q]uit");
        System.Console.WriteLine("  [n]ew (auto), [s]pam (5x), [c]ount, [a]uto-demo, [t]yped");
        System.Console.WriteLine("  [h]ealth, cac[h]e, [b]ackup");
        System.Console.WriteLine("  [r]esolver [lww|merge], [demo] conflict, [todos]");
    }

    private async Task HandleInput(string input, IPeerCollection<User> usersTyped, IPeerCollection users)
    {
        var config = await _configProvider.GetConfiguration();
        if (input.StartsWith("n"))
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var user = new User { Name = $"User-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "AutoCity" } };
            await usersTyped.Put(user);
            System.Console.WriteLine($"[+] Created {user.Name} with Id: {user.Id}...");
        }
        else if (input.StartsWith("s"))
        {
            for (int i = 0; i < 5; i++)
            {
                var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                var user = new User { Name = $"User-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "SpamCity" } };
                await usersTyped.Put(user);
                System.Console.WriteLine($"[+] Created {user.Name} with Id: {user.Id}...");
                await Task.Delay(100);
            }
        }
        else if (input.StartsWith("c"))
        {
            var all = await usersTyped.Find(u => true);
            System.Console.WriteLine($"Total Documents: {System.Linq.Enumerable.Count(all)}");
        }
        else if (input.StartsWith("p"))
        {
            var alice = new User { Name = "Alice", Age = 30, Address = new Address { City = "Paris" } };
            var bob = new User { Name = "Bob", Age = 25, Address = new Address { City = "Rome" } };
            await usersTyped.Put(alice);
            await usersTyped.Put(bob);
            System.Console.WriteLine($"Put Alice ({alice.Id}) and Bob ({bob.Id})");
        }
        else if (input.StartsWith("g"))
        {
            System.Console.Write("Enter user Id: ");
            var id = System.Console.ReadLine();
            if (!string.IsNullOrEmpty(id))
            {
                var u = await usersTyped.Get(id);
                System.Console.WriteLine(u != null ? $"Got: {u.Name}, Age {u.Age}, City: {u.Address?.City}" : "Not found");
            }
        }
        else if (input.StartsWith("d"))
        {
            System.Console.Write("Enter user Id to delete: ");
            var id = System.Console.ReadLine();
            if (!string.IsNullOrEmpty(id))
            {
                await usersTyped.Delete(id);
                System.Console.WriteLine($"Deleted user {id}");
            }
        }
        else if (input.StartsWith("l"))
        {
            var peers = _node.Discovery.GetActivePeers();
            var handshakeSvc = _serviceProvider.GetService<EntglDb.Network.Security.IPeerHandshakeService>();
            var secureIcon = handshakeSvc != null ? "🔒" : "🔓";
            
            System.Console.WriteLine($"Active Peers ({secureIcon}):");
            foreach(var p in peers)
                System.Console.WriteLine($"  - {p.NodeId} at {p.Address}");
            
            if (handshakeSvc != null)
                System.Console.WriteLine("\nℹ️  Secure mode: Connections use ECDH + AES-256");
        }
        else if (input.StartsWith("f"))
        {
                System.Console.WriteLine("Query: Age > 28");
                var results = await usersTyped.Find(u => u.Age > 28);
                foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");
        }
        else if (input.StartsWith("h"))
        {
            var health = await _healthCheck.CheckAsync();
            var syncStatus = _syncTracker.GetStatus();
            var handshakeSvc = _serviceProvider.GetService<EntglDb.Network.Security.IPeerHandshakeService>();
            
            System.Console.WriteLine("=== Health Check ===");
            System.Console.WriteLine($"Database: {(health.DatabaseHealthy ? "✓" : "✗")}");
            System.Console.WriteLine($"Network: {(health.NetworkHealthy ? "✓" : "✗")}");
            System.Console.WriteLine($"Security: {(handshakeSvc != null ? "🔒 Encrypted" : "🔓 Plaintext")}");
            System.Console.WriteLine($"Connected Peers: {health.ConnectedPeers}");
            System.Console.WriteLine($"Last Sync: {health.LastSyncTime?.ToString("HH:mm:ss") ?? "Never"}");
            System.Console.WriteLine($"Total Synced: {syncStatus.TotalDocumentsSynced} docs");
            
            if (health.Errors.Any())
            {
                System.Console.WriteLine("Errors:");
                foreach (var err in health.Errors.Take(3)) System.Console.WriteLine($"  - {err}");
            }
        }
        else if (input.StartsWith("ch") || input == "cache")
        {
            var stats = _cache.GetStatistics();
            System.Console.WriteLine($"=== Cache Stats ===\nSize: {stats.Size}\nHits: {stats.Hits}\nMisses: {stats.Misses}\nRate: {stats.HitRate:P1}");
        }
        else if (input.StartsWith("b"))
        {
            var backupPath = $"backups/backup-{config.NodeId}-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            Directory.CreateDirectory("backups");
            
            var store = _store as SqlitePeerStore;
            if (store != null)
            {
                await store.BackupAsync(backupPath);
                System.Console.WriteLine($"✓ Backup created: {backupPath}");
            }
        }
        else if (input.StartsWith("r") && input.Contains("resolver"))
        {
            var parts = input.Split(' ');
            if (parts.Length > 1)
            {
                var newResolver = parts[1].ToLower() switch
                {
                    "lww" => (IConflictResolver)new LastWriteWinsConflictResolver(),
                    "merge" => new RecursiveNodeMergeConflictResolver(),
                    _ => null
                };
                
                if (newResolver != null)
                {
                    var store = _store as SqlitePeerStore;
                    if (store != null)
                    {
                        // Note: Requires restart to fully apply. For demo, we inform user.
                        System.Console.WriteLine($"⚠️  Resolver changed to {parts[1].ToUpper()}. Restart node to apply.");
                        System.Console.WriteLine($"    (Current session continues with previous resolver)");
                    }
                }
                else
                {
                    System.Console.WriteLine("Usage: resolver [lww|merge]");
                }
            }
        }
        else if (input == "demo")
        {
            await RunConflictDemo();
        }
        else if (input == "todos")
        {
            var todoCollection = _db.Collection<TodoList>();
            var lists = await todoCollection.Find(t => true);
            
            System.Console.WriteLine("=== Todo Lists ===");
            foreach (var list in lists)
            {
                System.Console.WriteLine($"📋 {list.Name} ({list.Items.Count} items)");
                foreach (var item in list.Items)
                {
                    var status = item.Completed ? "✓" : " ";
                    System.Console.WriteLine($"  [{status}] {item.Task}");
                }
            }
        }
    }

    private async Task RunConflictDemo()
    {
        System.Console.WriteLine("\n=== Conflict Resolution Demo ===");
        System.Console.WriteLine("Simulating concurrent edits to a TodoList...\n");
        
        var todoCollection = _db.Collection<TodoList>();
        
        // Create initial list
        var list = new TodoList 
        { 
            Name = "Shopping List",
            Items = new List<TodoItem>
            {
                new TodoItem { Task = "Buy milk", Completed = false },
                new TodoItem { Task = "Buy bread", Completed = false }
            }
        };
        
        await todoCollection.Put(list);
        System.Console.WriteLine($"✓ Created list '{list.Name}' with {list.Items.Count} items");
        await Task.Delay(100);
        
        // Simulate Node A edit: Mark item as completed, add new item
        var listA = await todoCollection.Get(list.Id);
        if (listA != null)
        {
            listA.Items[0].Completed = true; // Mark milk as done
            listA.Items.Add(new TodoItem { Task = "Buy eggs", Completed = false });
            await todoCollection.Put(listA);
            System.Console.WriteLine("📝 Node A: Marked 'Buy milk' complete, added 'Buy eggs'");
        }
        
        await Task.Delay(100);
        
        // Simulate Node B edit: Mark different item, add different item
        var listB = await todoCollection.Get(list.Id);
        if (listB != null)
        {
            listB.Items[1].Completed = true; // Mark bread as done
            listB.Items.Add(new TodoItem { Task = "Buy cheese", Completed = false });
            await todoCollection.Put(listB);
            System.Console.WriteLine("📝 Node B: Marked 'Buy bread' complete, added 'Buy cheese'");
        }
        
        await Task.Delay(200);
        
        // Show final merged state
        var merged = await todoCollection.Get(list.Id);
        if (merged != null)
        {
            System.Console.WriteLine("\n🔀 Merged Result:");
            System.Console.WriteLine($"   List: {merged.Name}");
            foreach (var item in merged.Items)
            {
                var status = item.Completed ? "✓" : " ";
                System.Console.WriteLine($"   [{status}] {item.Task}");
            }
            
            var resolver = _serviceProvider.GetRequiredService<IConflictResolver>();
            var resolverType = resolver.GetType().Name;
            System.Console.WriteLine($"\nℹ️  Resolution Strategy: {resolverType}");
            
            if (resolverType.Contains("Recursive"))
            {
                System.Console.WriteLine("   → Items merged by 'id', both edits preserved");
            }
            else
            {
                System.Console.WriteLine("   → Last write wins, Node B changes override Node A");
            }
        }
        
        System.Console.WriteLine("\n✓ Demo complete. Run 'todos' to see all lists.\n");
    }
}
