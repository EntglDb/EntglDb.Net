using Avalonia.Markup.Xaml;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Sqlite;
using Lifter.Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Core.Sync;
using Microsoft.Extensions.Hosting;

namespace EntglDb.Test.Avalonia;

public class App : HostedApplication<MainView>
{
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure window settings
        services.ConfigureWindow(config =>
        {
            config.Title = "EntglDb Test - Avalonia";
            config.Width = 800;
            config.Height = 600;
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });

        // Configure database path
        var dbPath = configuration["Database:Path"];
        if (string.IsNullOrEmpty(dbPath))
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EntglDbTest");
            Directory.CreateDirectory(appDataPath);
            dbPath = Path.Combine(appDataPath, "entgldb-avalonia-test.db");
        }

        var nodeId = configuration["Database:NodeId"] ?? "test-node-avalonia";

        // Register EntglDb services
        
        // Conflict Resolution - Read from preferences (stored as "LWW" or "Merge")
        var resolverType = configuration["ConflictResolver"] ?? "Merge"; // Default to Merge for demo
        IConflictResolver resolver = resolverType == "Merge"
            ? new RecursiveNodeMergeConflictResolver()
            : new LastWriteWinsConflictResolver();
        
        services.AddSingleton<IConflictResolver>(resolver);
        
        services.AddSingleton<IPeerStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SqlitePeerStore>>();
            var conflictResolver = sp.GetRequiredService<IConflictResolver>();
            return new SqlitePeerStore($"Data Source={dbPath}", logger, conflictResolver);
        });

        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<IPeerStore>();
            // var logger = sp.GetRequiredService<ILogger<PeerDatabase>>(); 
            // PeerDatabase constructor might expect just (store, nodeId) or (store, nodeId, logger) depending on version.
            // Based on previous files, it's (store, nodeId).
            return new PeerDatabase(store, nodeId);
        });

        // Security - Always enabled for UI samples
        services.AddSingleton<IPeerHandshakeService, SecureHandshakeService>();
        
        // Network Configuration
        var tcpPort = configuration.GetValue<int>("EntglDb:Network:TcpPort", 0); // 0 = Random port
        var authToken = configuration["EntglDb:Node:AuthToken"] ?? "demo-secret-key";
        
        services.AddEntglDbNetwork(nodeId, tcpPort, authToken, useLocalhost: false);

        // Register Node Service to Start/Stop the node
        services.AddHostedService<EntglDbNodeService>();
    }
}