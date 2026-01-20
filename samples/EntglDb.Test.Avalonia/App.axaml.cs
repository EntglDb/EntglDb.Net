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

        // Configure base database path
        var basePath = configuration["Database:Path"];
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EntglDbTest");
        }

        // Register EntglDb Services using Fluent Extensions
        services.AddEntglDbCore()
                .AddEntglDbSqlite(options =>
                {
                    options.BasePath = basePath;
                    options.UsePerCollectionTables = true; // Use new per-collection tables
                })
                .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();

        // Register Node Service to Start/Stop the node
        services.AddHostedService<EntglDbNodeService>();
    }
}