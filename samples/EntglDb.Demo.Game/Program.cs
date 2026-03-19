using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Demo.Game;
using EntglDb.Network;
using EntglDb.Persistence.BLite;
using EntglDb.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding  = System.Text.Encoding.UTF8;

// Parse args: [nodeId] [tcpPort]
string nodeId = args.Length > 0 ? args[0] : $"hero-{new Random().Next(1000, 9999)}";
int tcpPort = args.Length > 1 ? int.Parse(args[1]) : new Random().Next(15000, 16000);

var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
Directory.CreateDirectory(dataPath);

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(dataPath, $"{nodeId}.log")));
builder.Logging.SetMinimumLevel(LogLevel.Trace);

// Peer configuration
var peerConfig = new StaticPeerNodeConfigurationProvider(
    new PeerNodeConfiguration
    {
        NodeId = nodeId,
        TcpPort = tcpPort,
        AuthToken = "DungeonCrawler-Secret"
    });

builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(peerConfig);

// Database
var databasePath = Path.Combine(dataPath, $"{nodeId}.blite");

// Register EntglDb with BLite
builder.Services.AddEntglDbCore()
    .AddEntglDbBLite<GameDbContext, GameDocumentStore>(sp => new GameDbContext(databasePath), databasePath + ".meta")
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>()
    .AddEntglDbSync();

// Game service
builder.Services.AddHostedService<GameService>();

var host = builder.Build();

Console.WriteLine($"  Database: {databasePath}");
Console.WriteLine($"  TCP Port: {tcpPort}");

await host.RunAsync();
