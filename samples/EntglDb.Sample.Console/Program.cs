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
using Microsoft.Extensions.Hosting;

using EntglDb.Sample.Shared;

namespace EntglDb.Sample.Console
{
    // Local User/Address classes removed in favor of Shared project

    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configuration
            builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
                               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            // Logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Options
            builder.Services.Configure<EntglDbOptions>(builder.Configuration.GetSection("EntglDb"));
            var options = new EntglDbOptions();
            builder.Configuration.GetSection("EntglDb").Bind(options);

            // Node ID
            string nodeId = args.Length > 0 ? args[0] : (builder.Configuration["Node:Id"] ?? "node-" + new Random().Next(1000, 9999));
            int tcpPort = args.Length > 1 ? int.Parse(args[1]) : options.Network.TcpPort;
            bool useLocalhost = args.Contains("--localhost");

            // Persistence
            string dbPath = options.Persistence.DatabasePath.Replace("data/", $"data-{nodeId}/");
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);

            // Register Services
            builder.Services.AddSingleton<IPeerStore>(sp => 
                new SqlitePeerStore($"Data Source={dbPath}", sp.GetRequiredService<ILogger<SqlitePeerStore>>()));

            builder.Services.AddSingleton(sp => new DocumentCache(
                options.Persistence.CacheSizeMb, sp.GetRequiredService<ILogger<DocumentCache>>()));
            
            builder.Services.AddSingleton(sp => new OfflineQueue(
                options.Sync.MaxQueueSize, sp.GetRequiredService<ILogger<OfflineQueue>>()));
            
            builder.Services.AddSingleton(sp => new SyncStatusTracker(
                sp.GetRequiredService<ILogger<SyncStatusTracker>>()));

            builder.Services.AddSingleton(sp => new RetryPolicy(
                sp.GetRequiredService<ILogger<RetryPolicy>>(),
                options.Network.RetryAttempts, options.Network.RetryDelayMs));
            
            builder.Services.AddSingleton<EntglDbHealthCheck>();

            // Network
            string authToken = "demo-secret-key"; 
            builder.Services.AddEntglDbNetwork(nodeId, tcpPort, authToken, useLocalhost);

            // Database
            builder.Services.AddSingleton<PeerDatabase>(sp => 
            {
                var store = sp.GetRequiredService<IPeerStore>();
                return new PeerDatabase(store, nodeId);
            });

            // Hosted Services (Lifter Pattern)
            builder.Services.AddHostedService<EntglDbNodeService>();       // Starts/Stops the Node
            builder.Services.AddHostedService<ConsoleInteractiveService>(); // Runs the Input Loop

            var host = builder.Build();
            await host.RunAsync();
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
