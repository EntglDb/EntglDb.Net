using EntglDb.Core.Extensions;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Network.Extensions;
using EntglDb.Persistence.Sqlite;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// EntglDB Configuration with Gap Detection
// ========================================

// 1. Configure node settings
builder.Services.Configure<PeerNodeConfiguration>(options =>
{
    options.NodeId = Environment.MachineName;
    options.TcpPort = 5000;
    options.UdpPort = 6000;
    options.AuthToken = builder.Configuration["EntglDb:AuthToken"] ?? "default-token";
});

builder.Services.AddSingleton<IPeerNodeConfigurationProvider, StaticPeerNodeConfigurationProvider>();

// 2. Configure SQLite persistence
builder.Services.Configure<SqlitePersistenceOptions>(options =>
{
    options.BasePath = "./data";
    options.DatabaseFilenameTemplate = "entgldb-{NodeId}.db";
    options.UsePerCollectionTables = true; // Optional: better performance for many collections
});

// 3. Register IPeerStore (with automatic migration support)
builder.Services.AddSingleton<IPeerStore>(sp =>
{
    var configProvider = sp.GetRequiredService<IPeerNodeConfigurationProvider>();
    var persistenceOptions = sp.GetRequiredService<IOptions<SqlitePersistenceOptions>>();
    var logger = sp.GetRequiredService<ILogger<SqlitePeerStore>>();
    
    // This will automatically run database migrations on startup
    return new SqlitePeerStore(configProvider, persistenceOptions.Value, logger);
});

// 4. Add Core Services (Gap Detection + Reconciliation)
builder.Services.AddEntglDbCore();

// 5. Add Network Services with Auto-Reconciliation on Startup
builder.Services.AddEntglDbReconciliation(options =>
{
    // ? Enable automatic reconciliation when the node starts
    options.EnableOnStartup = true;
    
    // Run in background to not block startup
    options.RunInBackground = true;
    
    // Warn if more than 30% of operations are legacy (without sequence numbers)
    options.LegacyDataThreshold = 0.3;
    
    // Process in batches of 500 operations
    options.BatchSize = 500;
});

// 6. Add Health Checks
builder.Services.AddHealthChecks()
    .AddEntglDbMigrationCheck()  // Monitors gap detection and migration status
    .AddCheck<CustomEntglDbHealthCheck>("entgldb"); // Your existing health check

// 7. Add controllers/APIs
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ========================================
// Configure HTTP Pipeline
// ========================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        });
        await context.Response.WriteAsync(result);
    }
});

// Specific health check for EntglDB
app.MapHealthChecks("/health/entgldb", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("entgldb")
});

app.UseAuthorization();
app.MapControllers();

// ========================================
// Optional: Manual Reconciliation Endpoint
// ========================================
app.MapPost("/api/admin/reconcile", async (IReconciliationService reconciliation) =>
{
    var analysis = await reconciliation.AnalyzeDatabaseAsync();
    
    if (!analysis.RecommendReconciliation)
    {
        return Results.Ok(new { 
            message = "No reconciliation needed",
            reason = analysis.Reason,
            legacyDataPercentage = analysis.LegacyDataPercentage
        });
    }
    
    var result = await reconciliation.PerformFullReconciliationAsync();
    
    return Results.Ok(new
    {
        success = result.Success,
        peersContacted = result.PeersContacted,
        operationsSynced = result.OperationsSynced,
        gapsDetected = result.GapsDetected,
        gapsFilled = result.GapsFilled,
        duration = result.Duration.ToString(),
        errors = result.Errors
    });
})
.RequireAuthorization(); // Protect admin endpoints

app.Run();

// ========================================
// Example Custom Health Check
// ========================================
public class CustomEntglDbHealthCheck : IHealthCheck
{
    private readonly IPeerStore _store;
    
    public CustomEntglDbHealthCheck(IPeerStore store)
    {
        _store = store;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if we can query the database
            var collections = await _store.GetCollectionsAsync(cancellationToken);
            var timestamp = await _store.GetLatestTimestampAsync(cancellationToken);
            
            return HealthCheckResult.Healthy("EntglDB is operational", new Dictionary<string, object>
            {
                ["collections"] = collections.Count(),
                ["latestTimestamp"] = timestamp.ToString()
            });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("EntglDB is not responding", ex);
        }
    }
}

// ========================================
// Example Configuration (appsettings.json)
// ========================================
/*
{
  "EntglDb": {
    "AuthToken": "your-secret-token-here",
    "Reconciliation": {
      "EnableOnStartup": true,
      "LegacyDataThreshold": 0.3,
      "BatchSize": 500
    }
  }
}
*/
