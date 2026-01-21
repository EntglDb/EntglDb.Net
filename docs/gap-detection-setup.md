# Gap Detection & Reconciliation - Setup Guide

## ?? Quick Start

### Basic Setup (Core Services Only)

```csharp
// Program.cs - Add core gap detection and reconciliation services
using EntglDb.Core.Extensions;

builder.Services.AddEntglDbCore(); // Adds IGapDetectionService and IReconciliationService

// Register your store
builder.Services.AddSingleton<IPeerStore, SqlitePeerStore>();
```

### Advanced Setup (Network with Auto-Reconciliation)

```csharp
using EntglDb.Network.Extensions;

// Program.cs - Full setup with automatic reconciliation on startup
builder.Services.AddEntglDbReconciliation(options =>
{
    // Enable automatic reconciliation when the node starts
    options.EnableOnStartup = true;
    
    // Run in background (non-blocking)
    options.RunInBackground = true;
    
    // Trigger warning if more than 50% of data is legacy (without sequence numbers)
    options.LegacyDataThreshold = 0.5;
    
    // Process 1000 operations per batch
    options.BatchSize = 1000;
});

// Add health checks to monitor migration and gap detection status
builder.Services.AddHealthChecks()
    .AddEntglDbMigrationCheck(
        name: "entgldb_gap_detection",
        tags: new[] { "entgldb", "database", "ready" }
    );

// Register your store with IPeerNodeConfigurationProvider (required for sequence numbers)
builder.Services.AddSingleton<IPeerStore>(sp =>
{
    var configProvider = sp.GetRequiredService<IPeerNodeConfigurationProvider>();
    var options = sp.GetRequiredService<IOptions<SqlitePersistenceOptions>>();
    var logger = sp.GetRequiredService<ILogger<SqlitePeerStore>>();
    
    return new SqlitePeerStore(configProvider, options.Value, logger);
});
```

## ?? Configuration Options

### appsettings.json

```json
{
  "EntglDb": {
    "GapDetection": {
      "MaxGapsPerNode": 1000,
      "MaintenanceIntervalHours": 1
    },
    "Reconciliation": {
      "EnableOnStartup": false,
      "RunInBackground": true,
      "LegacyDataThreshold": 0.5,
      "BatchSize": 1000
    }
  }
}
```

### Code-based Configuration

```csharp
// Separate configuration for gap detection
builder.Services.AddEntglDbGapDetection(options =>
{
    options.MaxGapsPerNode = 1000;
    options.MaintenanceInterval = TimeSpan.FromHours(1);
});

// Separate configuration for reconciliation
builder.Services.AddEntglDbReconciliation(options =>
{
    options.EnableOnStartup = true;
    options.RunInBackground = true;
    options.LegacyDataThreshold = 0.5;
    options.BatchSize = 1000;
});
```

## ?? Health Check Endpoints

```csharp
// Program.cs
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/entgldb", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("entgldb"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Sample Health Check Response

```json
{
  "status": "Healthy",
  "results": {
    "entgldb_migration": {
      "status": "Healthy",
      "description": "Gap detection operational. Current sequence: 1234",
      "data": {
        "CurrentSequenceNumber": 1234,
        "PeerNodesTracked": 3,
        "TotalOperationsTracked": 5678,
        "NodesWithGapTracking": 3,
        "KnownGaps": 0
      }
    }
  }
}
```

## ?? Manual Reconciliation

### Trigger Reconciliation Manually

```csharp
public class AdminController : ControllerBase
{
    private readonly IReconciliationService _reconciliation;
    
    [HttpPost("/api/admin/reconcile")]
    public async Task<IActionResult> TriggerReconciliation()
    {
        // Analyze first
        var analysis = await _reconciliation.AnalyzeDatabaseAsync();
        
        if (!analysis.RecommendReconciliation)
        {
            return Ok(new { 
                message = "No reconciliation needed",
                reason = analysis.Reason 
            });
        }
        
        // Perform full reconciliation
        var result = await _reconciliation.PerformFullReconciliationAsync();
        
        return Ok(new
        {
            success = result.Success,
            peersContacted = result.PeersContacted,
            gapsDetected = result.GapsDetected,
            gapsFilled = result.GapsFilled,
            duration = result.Duration,
            errors = result.Errors
        });
    }
}
```

## ?? Migration Scenarios

### Scenario 1: Fresh Installation
? **Automatic** - Database created with gap detection support from day 1.

```
Node starts ? SqlitePeerStore.Initialize()
            ? DatabaseMigrator checks version (v0)
            ? Creates tables with SequenceNumber column
            ? ? Ready to go!
```

### Scenario 2: Existing Database (Legacy)
? **Automatic Migration** on first startup with new version.

```
Node starts ? SqlitePeerStore.Initialize()
            ? DatabaseMigrator detects old schema (v0)
            ? Applies Migration v1 (adds SequenceNumber column)
            ? Logs: "Migrated to schema version 1"
            ? ? Gap detection enabled for new operations
            ? ?? Old operations have SequenceNumber=0
            ? Optional: Run reconciliation to analyze legacy data
```

### Scenario 3: Cluster with Mixed Versions
?? **Gradual Migration** - nodes can run mixed versions temporarily.

```
Cluster: Node-A (v1.0 - new), Node-B (v0.9 - old), Node-C (v0.9 - old)

1. Node-A migrates ? starts assigning sequence numbers
2. Node-A syncs with Node-B/C ? works (SequenceNumber=0 for old data)
3. Node-B upgrades ? migrates ? starts assigning sequence numbers
4. Node-C upgrades ? migrates ? starts assigning sequence numbers
5. Over time, more operations have sequence numbers
6. Gap detection improves as percentage of new data increases
```

## ?? Monitoring & Observability

### Metrics to Track

```csharp
// Custom metrics (Prometheus/Application Insights)
- entgldb_current_sequence_number
- entgldb_peer_nodes_tracked
- entgldb_total_operations_tracked
- entgldb_known_gaps_count
- entgldb_legacy_data_percentage
- entgldb_reconciliation_duration_seconds
```

### Logging Events

```
[Information] Current database schema version: 0
[Warning] Database schema migration required. Applying migrations...
[Information] Migrated to schema version 1 (Gap Detection support)
[Information] Database migrations completed successfully

[Warning] Reconciliation recommended: High percentage (65.3%) of legacy data detected
[Information] Starting automatic reconciliation on startup
[Information] Reconciliation completed successfully. Contacted 3 peers, detected 127 gaps in 00:00:12.4567
```

## ?? Troubleshooting

### Issue: "Migration never runs"
**Solution:** Ensure you're using the constructor with `IPeerNodeConfigurationProvider`.

```csharp
// ? Old way - no migration
var store = new SqlitePeerStore("Data Source=mydb.db");

// ? New way - migration runs
var store = new SqlitePeerStore(configProvider, options, logger);
```

### Issue: "Gap detection not working"
**Check:**
1. Sequence numbers are being assigned (check health endpoint)
2. `IPeerNodeConfigurationProvider` is properly configured
3. Database migration completed successfully (check logs)

```csharp
// Verify gap detection is working
var currentSeq = await store.GetCurrentSequenceNumberAsync();
if (currentSeq == 0)
{
    // Either no operations yet, or migration didn't run
}
```

### Issue: "High percentage of legacy data"
**Solution:** This is expected after migration. Gap detection will improve over time as new operations are created.

```csharp
// Check analysis
var analysis = await reconciliation.AnalyzeDatabaseAsync();
Console.WriteLine($"Legacy data: {analysis.LegacyDataPercentage:P1}");

// Optional: Trigger full reconciliation
if (analysis.LegacyDataPercentage > 0.8) // 80%+
{
    await reconciliation.PerformFullReconciliationAsync();
}
```

## ? Best Practices

1. **Enable reconciliation on startup** for production deployments
2. **Monitor health check endpoints** for degraded status
3. **Log migration events** to audit trails
4. **Plan migration windows** for large clusters (though it's non-blocking)
5. **Test migrations** in staging environment first
6. **Keep backups** before major version upgrades

## ?? Additional Resources

- [Gap Detection Architecture](./gap-detection-architecture.md)
- [Reconciliation Deep Dive](./reconciliation-deep-dive.md)
- [Migration Troubleshooting](./migration-troubleshooting.md)
- [Performance Tuning](./performance-tuning.md)
