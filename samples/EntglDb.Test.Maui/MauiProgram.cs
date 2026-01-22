using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; 
using System.Reflection; 
using Lifter.Maui; 
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.Sqlite;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Core.Network;
using Microsoft.Extensions.Hosting;

namespace EntglDb.Test.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.SupportHostedServices() // Enable Lifter IHostedService support
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		
		// Network Configuration
		// Configuration
		var assembly = typeof(App).Assembly;
		using var stream = assembly.GetManifestResourceStream("EntglDb.Test.Maui.appsettings.json");
		if (stream != null)
		{
			var configBuilder = new ConfigurationBuilder()
				.AddJsonStream(stream)
				.Build();
			builder.Configuration.AddConfiguration(configBuilder);
		}

		// Services
		builder.Services.AddSingleton<AppShell>();
        
        // Dashboard / Utility Pages
        builder.Services.AddTransient<NetworkPage>();
        builder.Services.AddTransient<DatabasePage>();
        builder.Services.AddTransient<LogsPage>();
        builder.Services.AddTransient<PayloadExchangePage>();
        builder.Services.AddTransient<TelemetryPage>();

        // Logging
        var logs = new System.Collections.Concurrent.ConcurrentQueue<LogEntry>();
        builder.Services.AddSingleton(logs);
        builder.Logging.AddProvider(new InMemoryLoggerProvider(logs));

		// EntglDb Services
		// Conflict Resolution - Read from preferences
		var resolverType = Preferences.Default.Get("ConflictResolver", "Merge");
		if (resolverType == "Merge")
		{
			builder.Services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
		}

        // Create/Retrieve Persistent Node Id
        var nodeId = Preferences.Default.Get("NodeId", string.Empty);
        if (string.IsNullOrEmpty(nodeId) || nodeId.StartsWith("Maui"))
        {
            nodeId = Guid.NewGuid().ToString();
            Preferences.Default.Set("NodeId", nodeId);
        }

		IPeerNodeConfigurationProvider peerNodeConfigurationProvider = new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
		{
			NodeId = $"CHANGEME-{nodeId}",
            TcpPort = 5001,
			AuthToken = "Test-Cluster-Key"
        });	

		builder.Services.AddSingleton(peerNodeConfigurationProvider);

        // EntglDb Core Services
        builder.Services.AddEntglDbCore()
						.AddEntglDbSqlite(options =>
						{
							options.BasePath = FileSystem.AppDataDirectory;
							options.UsePerCollectionTables = true; // Use new per-collection tables
						})
						.AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>(); // useHostedService = true by default

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

        return app;
	}
}
