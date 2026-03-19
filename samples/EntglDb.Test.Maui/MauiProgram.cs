using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; 
using System.Reflection; 
using Lifter.Maui; 
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using EntglDb.Sample.Shared;
using EntglDb.Network;
using EntglDb.Network.Security;
using EntglDb.Core.Network;
using EntglDb.Sync;
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
		// Conflict Resolution
		builder.Services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();

        // Config provider and db path resolved lazily inside factory lambdas,
        // after MAUI platform is fully initialized.
        builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(sp =>
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var nodeIdFile = Path.Combine(appDataPath, "entgldb-node-id.txt");
            string nodeId;
            if (File.Exists(nodeIdFile))
                nodeId = File.ReadAllText(nodeIdFile).Trim();
            else
            {
                nodeId = Guid.NewGuid().ToString();
                File.WriteAllText(nodeIdFile, nodeId);
            }

            return new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
            {
                NodeId = $"maui-{nodeId}",
                TcpPort = 5001,
                AuthToken = "Test-Cluster-Key",
                OplogRetentionHours = 2,
                MaintenanceIntervalMinutes = 5,
                KnownPeers = new List<KnownPeerConfiguration>
                {
                    new KnownPeerConfiguration
                    {
                        NodeId = "AspNetSampleNode",
#if ANDROID
                        Host = "10.0.2.2",
#else
                        Host = "localhost",
#endif
                        Port = 6001
                    }
                }
            });
        });

        // EntglDb Core Services
        builder.Services.AddEntglDbCore()
                        .AddEntglDbBLite<SampleDbContext, SampleDocumentStore>(
                            sp => new SampleDbContext(Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "app.blite")),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "app.blite.meta"))
                        .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>()
                        .AddEntglDbSync();
#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

        return app;
	}
}
