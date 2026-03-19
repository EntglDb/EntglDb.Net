using Avalonia.Markup.Xaml;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Persistence.BLite;
using EntglDb.Sample.Shared;
using EntglDb.Sync;
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

        // Configure base database path
        var basePath = configuration["Database:Path"];
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EntglDbTest");
        }
        Directory.CreateDirectory(basePath);
        var databasePath = Path.Combine(basePath, "avalonia.blite");

        // Register configuration provider
        var nodeId = configuration["Database:NodeId"] ?? "test-node-avalonia";
        var tcpPort = int.TryParse(configuration["EntglDb:Network:TcpPort"], out var p) ? p : 0;
        var authToken = configuration["EntglDb:Node:AuthToken"] ?? "demo-secret-key";
        IPeerNodeConfigurationProvider configProvider = new StaticPeerNodeConfigurationProvider(
            new PeerNodeConfiguration { NodeId = nodeId, TcpPort = tcpPort, AuthToken = authToken });
        services.AddSingleton<IPeerNodeConfigurationProvider>(configProvider);

        // Register EntglDb Services using Fluent Extensions
        services.AddEntglDbCore()
                .AddEntglDbBLite<SampleDbContext, SampleDocumentStore>(sp => new SampleDbContext(databasePath), databasePath + ".meta")
                .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>()
                .AddEntglDbSync();
    }
}