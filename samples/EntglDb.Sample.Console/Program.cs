using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Cache;
using EntglDb.Core.Sync;
using EntglDb.Core.Diagnostics;
using EntglDb.Core.Resilience;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Persistence.BLite;
using EntglDb.Sample.Shared;
using Microsoft.Extensions.Hosting;
using EntglDb.Core.Network;

namespace EntglDb.Sample.Console;

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

        var randomPort = new Random().Next(1000, 9999);
        // Node ID
        string nodeId = args.Length > 0 ? args[0] : ("node-" + randomPort);
        int tcpPort = args.Length > 1 ? int.Parse(args[1]) : randomPort;


        // Conflict Resolution Strategy (can be switched at runtime via service replacement)
        var useRecursiveMerge = args.Contains("--merge");
        if (useRecursiveMerge)
        {
            builder.Services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
        }

        IPeerNodeConfigurationProvider peerNodeConfigurationProvider = new StaticPeerNodeConfigurationProvider(
            new PeerNodeConfiguration
            {
                NodeId = nodeId,
                TcpPort = tcpPort,
                AuthToken = "Test-Cluster-Key",
                //KnownPeers = builder.Configuration.GetSection("EntglDb:KnownPeers").Get<List<KnownPeerConfiguration>>() ?? new()
            });

        builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(peerNodeConfigurationProvider);

        // Database path
        var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        Directory.CreateDirectory(dataPath);
        var databasePath = Path.Combine(dataPath, $"{nodeId}.blite");

        // Register EntglDb Services using Fluent Extensions with BLite, SampleDbContext, and SampleDocumentStore
        builder.Services.AddEntglDbCore()
                        .AddEntglDbBLite<SampleDbContext, SampleDocumentStore>(sp => new SampleDbContext(databasePath))
                        .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>(); // useHostedService = true by default
        
        builder.Services.AddHostedService<ConsoleInteractiveService>(); // Runs the Input Loop

        var host = builder.Build();
        
        // Force OplogCoordinator initialization - it subscribes to IDocumentStore events
        _ = host.Services.GetRequiredService<OplogCoordinator>();
        
        System.Console.WriteLine($"? Node {nodeId} initialized on port {tcpPort}");
        System.Console.WriteLine($"? Database: {databasePath}");
        System.Console.WriteLine($"? OplogCoordinator active - tracking local changes");
        System.Console.WriteLine();
        
        await host.RunAsync();
    }

    private class StaticPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
    {
        public PeerNodeConfiguration Configuration { get; set; }

        public StaticPeerNodeConfigurationProvider(PeerNodeConfiguration configuration)
        {
            Configuration = configuration;
        }

        public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

        public Task<PeerNodeConfiguration> GetConfiguration()
        {
            return Task.FromResult(Configuration);
        }

        protected virtual void OnConfigurationChanged(PeerNodeConfiguration newConfig)
        {
            ConfigurationChanged?.Invoke(this, newConfig);
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
