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
		if (resolverType == "Merge")
		{
			builder.Services.AddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();
		}
		// If LWW is desired, the default fallback in AddEntglDbSqlite will handle it, or we could explicitely register LastWriteWinsConflictResolver here.
		
		// Use a factory or pre-calculate path? 
		// Since AddEntglDbSqlite takes a string, we need to resolve the path here.
		var dataDir = FileSystem.AppDataDirectory;
		if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
		var dbPath = Path.Combine(dataDir, "entgldb-maui.db");

		builder.Services.AddEntglDbCore()
						.AddEntglDbSqlite($"Data Source={dbPath}")
						.AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();

        builder.Services.AddHostedService<EntglDbNodeService>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
