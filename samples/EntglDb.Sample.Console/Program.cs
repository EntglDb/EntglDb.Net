using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Configuration;
using EntglDb.Core.Storage;
using EntglDb.Core.Cache;
using EntglDb.Core.Sync;
using EntglDb.Core.Diagnostics;
using EntglDb.Core.Resilience;
using EntglDb.Network;
using EntglDb.Persistence.Sqlite;
using EntglDb.Core.Metadata;
using EntglDb.Sample.Console.Mocks;

namespace EntglDb.Sample.Console
{
    public class User
    {
        [PrimaryKey(AutoGenerate = true)]
        public string Id { get; set; } = "";
        
        public string? Name { get; set; }
        
        [Indexed]
        public int Age { get; set; }
        
        public Address? Address { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // Load configuration from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // Parse Node ID from args or random
            string nodeId = args.Length > 0 ? args[0] : "node-" + new Random().Next(1000, 9999);
            
            var services = new ServiceCollection();
            
            // Configure EntglDb options from appsettings.json
            services.Configure<EntglDbOptions>(configuration.GetSection("EntglDb"));
            var options = new EntglDbOptions();
            configuration.GetSection("EntglDb").Bind(options);
            
            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Parse Loopback mode
            bool useLocalhost = System.Linq.Enumerable.Contains(args, "--localhost");
            int tcpPort = args.Length > 1 ? int.Parse(args[1]) : options.Network.TcpPort;

            // Persistence with logger
            string dbPath = options.Persistence.DatabasePath.Replace("data/", $"data-{nodeId}/");
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }
            
            services.AddSingleton<IPeerStore>(sp => 
                new SqlitePeerStore($"Data Source={dbPath}", sp.GetService<ILogger<SqlitePeerStore>>()));
            
            // Production features
            services.AddSingleton(sp => new DocumentCache(
                options.Persistence.CacheSizeMb, 
                sp.GetService<ILogger<DocumentCache>>()));
            
            services.AddSingleton(sp => new OfflineQueue(
                options.Sync.MaxQueueSize,
                sp.GetService<ILogger<OfflineQueue>>()));
            
            services.AddSingleton(sp => new SyncStatusTracker(
                sp.GetService<ILogger<SyncStatusTracker>>()));
            
            services.AddSingleton(sp => new RetryPolicy(
                sp.GetRequiredService<ILogger<RetryPolicy>>(),
                options.Network.RetryAttempts,
                options.Network.RetryDelayMs));
            
            services.AddSingleton<EntglDbHealthCheck>();
            
            // Networking
            string authToken = "demo-secret-key";
            services.AddEntglDbNetwork(nodeId, tcpPort, authToken, useLocalhost);

            var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Starting EntglDb node {NodeId} on port {Port}", nodeId, tcpPort);
            logger.LogInformation("Database: {DbPath}", dbPath);
            logger.LogInformation("Cache size: {Size}MB", options.Persistence.CacheSizeMb);

            // Start Components
            var node = provider.GetRequiredService<EntglDbNode>();
            node.Start();

            // Setup DB
            var store = provider.GetRequiredService<IPeerStore>();
            var cache = provider.GetRequiredService<DocumentCache>();
            var queue = provider.GetRequiredService<OfflineQueue>();
            var healthCheck = provider.GetRequiredService<EntglDbHealthCheck>();
            
            var db = new PeerDatabase(store, nodeId); 
            await db.InitializeAsync();

            // Non-generic API (still supported)
            var users = db.Collection("users");
            
            // Generic API (new, type-safe)
            var usersTyped = db.Collection<User>();

            System.Console.WriteLine($"--- Started {nodeId} on Port {tcpPort} ---");
            System.Console.WriteLine("Commands:");
            System.Console.WriteLine("  [p]ut, [g]et, [d]elete, [f]ind, [l]ist peers, [q]uit");
            System.Console.WriteLine("  [n]ew (auto), [s]pam (5x), [c]ount, [a]uto-demo, [t]yped");
            System.Console.WriteLine("  [h]ealth, cac[h]e, [b]ackup");


            while (true)
            {
                var input = System.Console.ReadLine();
                if (string.IsNullOrEmpty(input)) continue;

                if (input.StartsWith("n"))
                {
                    var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                    var user = new User { Name = $"User-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "AutoCity" } };
                    await usersTyped.Put(user); // Auto-generates Id
                    System.Console.WriteLine($"[+] Created {user.Name} with Id: {user.Id.Substring(0, 8)}...");
                }
                else if (input.StartsWith("s"))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                        var user = new User { Name = $"User-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "SpamCity" } };
                        await usersTyped.Put(user); // Auto-generates Id
                        System.Console.WriteLine($"[+] Created {user.Name} with Id: {user.Id.Substring(0, 8)}...");
                        await Task.Delay(100);
                    }
                }
                else if (input.StartsWith("c"))
                {
                   // Count using generic API
                   var all = await usersTyped.Find(u => true);
                   System.Console.WriteLine($"Total Documents: {System.Linq.Enumerable.Count(all)}");
                }
                else if (input.StartsWith("p"))
                {
                    var alice = new User { Name = "Alice", Age = 30, Address = new Address { City = "Paris" } };
                    var bob = new User { Name = "Bob", Age = 25, Address = new Address { City = "Rome" } };
                    await usersTyped.Put(alice);
                    await usersTyped.Put(bob);
                    System.Console.WriteLine($"Put Alice (Id: {alice.Id.Substring(0, 8)}...) and Bob (Id: {bob.Id.Substring(0, 8)}...)");
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
                    var peers = node.Discovery.GetActivePeers();
                    System.Console.WriteLine("Active Peers:");
                    foreach(var p in peers)
                    {
                        System.Console.WriteLine($"- {p.NodeId} at {p.Address}");
                    }
                }
                else if (input.StartsWith("f"))
                {
                     // Demo Find with generic API
                     System.Console.WriteLine("Query: Age > 28");
                     var results = await usersTyped.Find(u => u.Age > 28);
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Name == 'Bob'");
                     results = await usersTyped.Find(u => u.Name == "Bob");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Address.City == 'Rome'");
                     results = await usersTyped.Find(u => u.Address!.City == "Rome");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} in {u.Address?.City}");

                     System.Console.WriteLine("Query: Age >= 30");
                     results = await usersTyped.Find(u => u.Age >= 30);
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Name != 'Bob'");
                     results = await users.Find<User>(u => u.Name != "Bob");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name}");
                }
                else if (input.StartsWith("f2"))
                {
                     // Demo Find with Paging
                     System.Console.WriteLine("Query: All users (Skip 5, Take 2)");
                     var results = await usersTyped.Find(u => true, 5, 2);
                     foreach(var u in results) System.Console.WriteLine($"Found Page: {u.Name} ({u.Age})");
                }
                else if (input.StartsWith("a"))
                {
                     // Demo Auto-Generated Keys
                     System.Console.WriteLine("=== Auto-Generated Primary Keys Demo ===");
                     
                     // Create users without specifying keys - IDs auto-generated
                     var user1 = new User { Name = "AutoUser1", Age = 25, Address = new Address { City = "AutoCity1" } };
                     await usersTyped.Put(user1);
                     System.Console.WriteLine($"Created: {user1.Name} with auto-generated Id: {user1.Id}");
                     
                     var user2 = new User { Name = "AutoUser2", Age = 35, Address = new Address { City = "AutoCity2" } };
                     await usersTyped.Put(user2);
                     System.Console.WriteLine($"Created: {user2.Name} with auto-generated Id: {user2.Id}");
                     
                     // Retrieve by auto-generated ID
                     var retrieved = await usersTyped.Get(user1.Id);
                     System.Console.WriteLine($"Retrieved: {retrieved?.Name} (Age: {retrieved?.Age})");
                }
                else if (input.StartsWith("t"))
                {
                     // Demo Generic API
                     System.Console.WriteLine("=== Generic Collection<User> API Demo ===");
                     
                     // Put with type inference
                     await usersTyped.Put("typed-user", new User { Name = "TypedUser", Age = 42, Address = new Address { City = "TypeCity" } });
                     System.Console.WriteLine("Put: typed-user");
                     
                     // Get without type parameter
                     var typedUser = await usersTyped.Get("typed-user");
                     System.Console.WriteLine($"Get: {typedUser?.Name} (Age: {typedUser?.Age})");
                     
                     // Find without type parameter
                     var typedResults = await usersTyped.Find(u => u.Age > 30);
                     System.Console.WriteLine($"Find (Age > 30): {System.Linq.Enumerable.Count(typedResults)} results");
                     foreach(var u in typedResults) System.Console.WriteLine($"  - {u.Name} ({u.Age})");
                }
                else if (input.StartsWith("h"))
                {
                    var syncTracker = provider.GetRequiredService<SyncStatusTracker>();
                    var health = await healthCheck.CheckAsync();
                    var syncStatus = syncTracker.GetStatus();
                    
                    System.Console.WriteLine("=== Health Check ===");
                    System.Console.WriteLine($"Database: {(health.DatabaseHealthy ? "✓" : "✗")}");
                    System.Console.WriteLine($"Network: {(health.NetworkHealthy ? "✓" : "✗")}");
                    System.Console.WriteLine($"Connected Peers: {health.ConnectedPeers}");
                    System.Console.WriteLine($"Last Sync: {health.LastSyncTime?.ToString("HH:mm:ss") ?? "Never"}");
                    System.Console.WriteLine($"Pending Ops: {queue.Count}");
                    System.Console.WriteLine($"Total Synced: {syncStatus.TotalDocumentsSynced} docs");
                    
                    if (health.Errors.Any())
                    {
                        System.Console.WriteLine("Errors:");
                        foreach (var err in health.Errors.Take(3))
                            System.Console.WriteLine($"  - {err}");
                    }
                }
                else if (input.StartsWith("ch") || input == "cache")
                {
                    var stats = cache.GetStatistics();
                    System.Console.WriteLine("=== Cache Statistics ===");
                    System.Console.WriteLine($"Size: {stats.Size} documents");
                    System.Console.WriteLine($"Hits: {stats.Hits}");
                    System.Console.WriteLine($"Misses: {stats.Misses}");
                    System.Console.WriteLine($"Hit Rate: {stats.HitRate:P1}");
                }
                else if (input.StartsWith("b"))
                {
                    var backupPath = $"backups/backup-{nodeId}-{DateTime.Now:yyyyMMdd-HHmmss}.db";
                    Directory.CreateDirectory("backups");
                    
                    var sqliteStore = store as SqlitePeerStore;
                    if (sqliteStore != null)
                    {
                        await sqliteStore.BackupAsync(backupPath);
                        System.Console.WriteLine($"✓ Backup created: {backupPath}");
                    }
                    else
                    {
                        System.Console.WriteLine("Backup not supported for this store type");
                    }
                }
                else if (input.StartsWith("q"))
                {
                    break;
                }
            }

            node.Stop();
        }
    }

    public class SimpleFileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        public SimpleFileLoggerProvider(string path) => _path = path;
        public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, _path);
        public void Dispose() { }
    }

    public class SimpleFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private static object _lock = new object();

        public SimpleFileLogger(string category, string path)
        {
            _category = category;
            _path = path;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            
            var msg = $"{DateTime.Now:O} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception != null) msg += $"\n{exception}";

            // Simple append, no retry needed for unique files
            try 
            {
               File.AppendAllText(_path, msg + Environment.NewLine);
            }
            catch { /* Ignore logging errors */ }
        }
    }
}
