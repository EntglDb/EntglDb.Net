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

			// Register PeerNodeConfiguration manually for StaticPeerNodeConfigurationProvider
			var peerConfig = new PeerNodeConfiguration();
			configBuilder.GetSection("EntglDb:Node").Bind(peerConfig);
			configBuilder.GetSection("EntglDb:Network").Bind(peerConfig);
			// Fallback/Validation
			if (string.IsNullOrEmpty(peerConfig.NodeId)) peerConfig.NodeId = Guid.NewGuid().ToString();
			
			builder.Services.AddSingleton(peerConfig);
		}

		// Services
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<MainPage>();


		// EntglDb Services
		// Conflict Resolution - Read from preferences
		var resolverType = Preferences.Default.Get("ConflictResolver", "Merge");
		if (resolverType == "Merge")
		{
			builder.Services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
		}
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

		return builder.Build();
	}
}
