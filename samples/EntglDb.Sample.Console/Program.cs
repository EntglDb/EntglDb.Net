using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Network;
using EntglDb.Persistence.Sqlite;
using EntglDb.Sample.Console.Mocks;

namespace EntglDb.Sample.Console
{
    public class User
    {
        public string? Name { get; set; }
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
            // Parse Node ID from args or random
            string nodeId = args.Length > 0 ? args[0] : "node-" + new Random().Next(1000, 9999);
            // Parse Port or random
            int tcpPort = args.Length > 1 ? int.Parse(args[1]) : 5001 + new Random().Next(0, 100);

            var services = new ServiceCollection();
            
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            // Parse Loopback mode
            bool useLocalhost = System.Linq.Enumerable.Contains(args, "--localhost");


            // Persistence
            string dbPath = $"EntglDb-{nodeId}.db";
            services.AddSingleton<IPeerStore>(sp => new SqlitePeerStore($"Data Source={dbPath}"));
            
            // Networking
            string authToken = "demo-secret-key"; // Hardcoded for demo
            services.AddEntglDbNetwork(nodeId, tcpPort, authToken, useLocalhost);

            // Simple Error File Logging
            int logArgIndex = Array.IndexOf(args, "--error-log");
            if (logArgIndex >= 0 && logArgIndex < args.Length - 1)
            {
                string logFile = args[logArgIndex + 1];
                services.AddLogging(builder => builder.AddProvider(new SimpleFileLoggerProvider(logFile)));
            }

            var provider = services.BuildServiceProvider();

            // Start Components
            // Start Components
            var node = provider.GetRequiredService<EntglDbNode>();
            node.Start();

            // Setup DB
            var store = provider.GetRequiredService<IPeerStore>();
            var db = new PeerDatabase(store, nodeId); 
            await db.InitializeAsync();

            var users = db.Collection("users");

            System.Console.WriteLine($"--- Started {nodeId} on Port {tcpPort} ---");
            System.Console.WriteLine("Commands: [p]ut, [g]et, [d]elete, [l]ist peers, [q]uit, [f]ind");
            System.Console.WriteLine("          [n]ew (auto-generate), [s]pam (5x auto), [c]ount");

            while (true)
            {
                var input = System.Console.ReadLine();
                if (string.IsNullOrEmpty(input)) continue;

                if (input.StartsWith("n"))
                {
                    var id = Guid.NewGuid().ToString().Substring(0, 8);
                    var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                    var user = new User { Name = $"User-{id}-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "AutoCity" } };
                    await users.Put($"user-{id}", user);
                    System.Console.WriteLine($"[+] Created user-{id} at {ts}");
                }
                else if (input.StartsWith("s"))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var id = Guid.NewGuid().ToString().Substring(0, 8);
                        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                        var user = new User { Name = $"User-{id}-{ts}", Age = new Random().Next(18, 90), Address = new Address { City = "SpamCity" } };
                        await users.Put($"user-{id}", user);
                        System.Console.WriteLine($"[+] Created user-{id} at {ts}");
                        await Task.Delay(100);
                    }
                }
                else if (input.StartsWith("c"))
                {
                   // Hacky count using Find all
                   var all = await users.Find<User>(u => true);
                   System.Console.WriteLine($"Total Documents: {System.Linq.Enumerable.Count(all)}");
                }
                else if (input.StartsWith("p"))
                {
                    await users.Put("user1", new User { Name = "Alice", Age = 30, Address = new Address { City = "Paris" } });
                    await users.Put("user2", new User { Name = "Bob", Age = 25, Address = new Address { City = "Rome" } });
                    System.Console.WriteLine("Put users");
                }
                else if (input.StartsWith("g"))
                {
                    var u = await users.Get<User>("user1");
                    System.Console.WriteLine($"Get user1: {u?.Name} Age: {u?.Age}");
                }
                else if (input.StartsWith("d"))
                {
                    await users.Delete("user1");
                    System.Console.WriteLine("Deleted user1");
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
                     // Demo Find
                     System.Console.WriteLine("Query: Age > 28");
                     var results = await users.Find<User>(u => u.Age > 28);
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Name == 'Bob'");
                     results = await users.Find<User>(u => u.Name == "Bob");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Address.City == 'Rome'");
                     results = await users.Find<User>(u => u.Address!.City == "Rome");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} in {u.Address?.City}");

                     System.Console.WriteLine("Query: Age >= 30");
                     results = await users.Find<User>(u => u.Age >= 30);
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name} ({u.Age})");

                     System.Console.WriteLine("Query: Name != 'Bob'");
                     results = await users.Find<User>(u => u.Name != "Bob");
                     foreach(var u in results) System.Console.WriteLine($"Found: {u.Name}");
                }
                else if (input.StartsWith("f2"))
                {
                     // Demo Find with Paging
                     System.Console.WriteLine("Query: All users (Skip 5, Take 2)");
                     // Pass predicate true, skip 5, take 2
                     var results = await users.Find<User>(u => true, 5, 2);
                     foreach(var u in results) System.Console.WriteLine($"Found Page: {u.Name} ({u.Age})");
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
