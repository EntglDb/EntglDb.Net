using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Demo.Game;
using EntglDb.Demo.Game.MonoGame;
using EntglDb.Network;
using EntglDb.Persistence.BLite;
using EntglDb.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Parse args: [nodeId] [tcpPort]
string nodeId  = args.Length > 0 ? args[0] : $"hero-{new Random().Next(1000, 9999)}";
int    tcpPort = args.Length > 1 ? int.Parse(args[1]) : new Random().Next(15000, 16000);

var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
Directory.CreateDirectory(dataPath);

var builder = Host.CreateApplicationBuilder(args);

// Host.CreateApplicationBuilder automatically adds appsettings.json
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Peer configuration
var peerConfig = new StaticPeerNodeConfigurationProvider(
    new PeerNodeConfiguration
    {
        NodeId   = nodeId,
        TcpPort  = tcpPort,
        AuthToken = "DungeonCrawler-Secret"
    });

builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(peerConfig);

// Database path per node
var databasePath = Path.Combine(dataPath, $"{nodeId}.blite");

// Register EntglDb with BLite
builder.Services.AddEntglDbCore()
    .AddEntglDbBLite<GameDbContext, GameDocumentStore>(sp => new GameDbContext(databasePath), databasePath + ".meta")
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>()
    .AddEntglDbSync();

// Build DI container
var host = builder.Build();

// Start background sync in a background thread; game loop runs on the main thread
var cts = new CancellationTokenSource();
var hostTask = host.StartAsync(cts.Token);

// Resolve dependencies
var db  = host.Services.GetRequiredService<GameDbContext>();
var rng = new Random();

// GameEngine is a lightweight object, not a DI-registered service
var engine = new GameEngine(db, nodeId, rng);

try
{
    using var game = new DungeonGame(engine, nodeId);
    game.Run();
}
finally
{
    cts.Cancel();
    await hostTask;
    await host.StopAsync();
}
