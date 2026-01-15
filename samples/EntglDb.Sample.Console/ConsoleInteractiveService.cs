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

namespace EntglDb.Sample.Console;

public class ConsoleInteractiveService : BackgroundService
{
    private readonly ILogger<ConsoleInteractiveService> _logger;
    private readonly PeerDatabase _db;
    private readonly EntglDbNode _node;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IPeerStore _store; // Injected Store
    
    // Auxiliary services for status/commands
    private readonly DocumentCache _cache;
    private readonly OfflineQueue _queue;
    private readonly EntglDbHealthCheck _healthCheck;
    private readonly SyncStatusTracker _syncTracker;

    public ConsoleInteractiveService(
        ILogger<ConsoleInteractiveService> logger,
        PeerDatabase db,
        EntglDbNode node,
        IHostApplicationLifetime lifetime,
        IPeerStore store, // Inject IPeerStore
        DocumentCache cache,
        OfflineQueue queue,
        EntglDbHealthCheck healthCheck,
        SyncStatusTracker syncTracker)
    {
        _logger = logger;
        _db = db;
        _node = node;
        _lifetime = lifetime;
        _store = store; // Store it
        _cache = cache;
        _queue = queue;
        _healthCheck = healthCheck;
        _syncTracker = syncTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for DB initialization (could be moved to IHostedService StartAsync in DB service if separate)
        await _db.InitializeAsync(stoppingToken);
        
        // Use Type-Safe Collections
        var usersTyped = _db.Collection<User>();
        var users = _db.Collection("users"); // Legacy/Dynamic

        System.Console.WriteLine($"--- Interactive Console ---");
        System.Console.WriteLine($"Node ID: {_db.NodeId}");
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
    }

    private async Task HandleInput(string input, IPeerCollection<User> usersTyped, IPeerCollection users)
    {
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
            System.Console.WriteLine("Active Peers:");
            foreach(var p in peers)
                System.Console.WriteLine($"- {p.NodeId} at {p.Address}");
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
            
            System.Console.WriteLine("=== Health Check ===");
            System.Console.WriteLine($"Database: {(health.DatabaseHealthy ? "✓" : "✗")}");
            System.Console.WriteLine($"Network: {(health.NetworkHealthy ? "✓" : "✗")}");
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
            var backupPath = $"backups/backup-{_db.NodeId}-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            Directory.CreateDirectory("backups");
            
            var store = _store as SqlitePeerStore;
            if (store != null)
            {
                await store.BackupAsync(backupPath);
                System.Console.WriteLine($"✓ Backup created: {backupPath}");
            }
        }
    }
}
