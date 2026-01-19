using Avalonia.Markup.Xaml;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Persistence.Sqlite;
using Lifter.Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

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
        

        // Conflict Resolution - Read from preferences (stored as "LWW" or "Merge")
        var resolverType = configuration["ConflictResolver"] ?? "Merge"; // Default to Merge for demo
        if (resolverType == "Merge")
        {
            services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
        }
        
        // Network Configuration
        var tcpPort = configuration.GetValue<int>("EntglDb:Network:TcpPort", 0); // 0 = Random port
        var authToken = configuration["EntglDb:Node:AuthToken"] ?? "demo-secret-key";

        // Register EntglDb Services using Fluent Extensions
        services.AddEntglDbCore()
                .AddEntglDbSqlite($"Data Source={dbPath}")
                .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();

        // Register Node Service to Start/Stop the node
        services.AddHostedService<EntglDbNodeService>();
    }
}