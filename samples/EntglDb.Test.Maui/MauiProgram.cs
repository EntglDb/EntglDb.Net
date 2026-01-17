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

		// Security - Always enabled for UI samples
		builder.Services.AddSingleton<IPeerHandshakeService, SecureHandshakeService>();
		
		// Network Configuration
		// Configuration
		var assembly = typeof(App).Assembly;
		using var stream = assembly.GetManifestResourceStream("EntglDb.Test.Maui.appsettings.json");
		if (stream != null)
		{
			var config = new ConfigurationBuilder()
				.AddJsonStream(stream)
				.Build();
			builder.Configuration.AddConfiguration(config);
		}

		// Services
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<MainPage>();

		// EntglDb Services
		// Conflict Resolution - Read from preferences
		var resolverType = Preferences.Default.Get("ConflictResolver", "Merge");
		IConflictResolver resolver = resolverType == "Merge"
			? new RecursiveNodeMergeConflictResolver()
			: new LastWriteWinsConflictResolver();
		
		builder.Services.AddSingleton<IConflictResolver>(resolver);
		
		builder.Services.AddSingleton<IPeerStore>(sp => 
		{
			var config = sp.GetRequiredService<IConfiguration>();
			var dataDir = config["DataDirectory"];
			if (dataDir == "FileSystem.AppDataDirectory" || string.IsNullOrEmpty(dataDir))
			{
				dataDir = FileSystem.AppDataDirectory;
			}

			// Ensure directory exists
			if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

			var dbPath = Path.Combine(dataDir, "entgldb-maui.db");
			var conflictResolver = sp.GetRequiredService<IConflictResolver>();
			return new SqlitePeerStore($"Data Source={dbPath}", sp.GetRequiredService<ILogger<SqlitePeerStore>>(), conflictResolver);
		});

		
        var mauiNodeId = "maui-node-" + Guid.NewGuid().ToString().Substring(0, 8); // Fallback

		builder.Services.AddSingleton<PeerDatabase>(sp =>
		{
			var store = sp.GetRequiredService<IPeerStore>();
			var config = sp.GetRequiredService<IConfiguration>();
			var nodeId = config["EntglDb:Node:Id"] ?? mauiNodeId;
			
			return new PeerDatabase(store, nodeId);
		});

        // Network
        // Reading directly from built config to get values for Network registration
        var builtConfig = builder.Configuration;
        var nodeId = builtConfig["EntglDb:Node:Id"] ?? mauiNodeId;
        var tcpPort = builtConfig.GetValue<int>("EntglDb:Network:TcpPort", 0);
        var authToken = builtConfig["EntglDb:Node:AuthToken"] ?? "demo-secret-key";

        builder.Services.AddEntglDbNetwork(nodeId, tcpPort, authToken, useLocalhost: false);
        builder.Services.AddHostedService<EntglDbNodeService>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
