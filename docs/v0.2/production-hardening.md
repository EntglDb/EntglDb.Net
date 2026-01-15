# Production Hardening - Implementation Guide

## Quick Reference

### Configuration (appsettings.json)
```json
{
  "EntglDb": {
    "Network": {
      "TcpPort": 5000,
      "UdpPort": 6000,
      "RetryAttempts": 3
    },
    "Persistence": {
      "DatabasePath": "data/entgldb.db",
      "EnableWalMode": true,
      "CacheSizeMb": 50,
      "EnableAutoBackup": true,
      "BackupPath": "backups/"
    },
    "Sync": {
      "EnableOfflineQueue": true,
      "MaxQueueSize": 1000
    }
  }
}
```

### DI Setup
```csharp
services.Configure<EntglDbOptions>(configuration.GetSection("EntglDb"));
services.AddSingleton<RetryPolicy>();
services.AddSingleton<DocumentCache>();
services.AddSingleton<OfflineQueue>();
services.AddSingleton<SyncStatusTracker>();
services.AddSingleton<EntglDbHealthCheck>();
```

### Health Check
```csharp
var healthCheck = serviceProvider.GetRequiredService<EntglDbHealthCheck>();
var status = await healthCheck.CheckAsync();

Console.WriteLine($"Database: {status.DatabaseHealthy}");
Console.WriteLine($"Network: {status.NetworkHealthy}");
Console.WriteLine($"Peers: {status.ConnectedPeers}");
```

### Offline Queue
```csharp
// Enqueue during offline
if (!isOnline)
{
    offlineQueue.Enqueue(new PendingOperation 
    { 
        Type = "put", 
        Collection = "users", 
        Key = "user1",
        Data = user 
    });
}

// Flush when back online
var (successful, failed) = await offlineQueue.FlushAsync(async op => 
{
    var collection = database.Collection(op.Collection);
    if (op.Type == "put" && op.Data != null)
        await collection.Put(op.Key, op.Data);
    else if (op.Type == "delete")
        await collection.Delete(op.Key);
});
```

### Document Cache
```csharp
var cache = new DocumentCache(maxSizeMb: 50);

// Check cache first
var cached = cache.Get("users", "user1");
if (cached != null) return cached;

// Load from database
var doc = await store.GetDocumentAsync("users", "user1");
if (doc != null) cache.Set("users", "user1", doc);
```

### SQLite Backup
```csharp
await store.BackupAsync("backups/backup-20260115.db");
```

### Retry Policy
```csharp
var retry = new RetryPolicy(logger, maxAttempts: 3, delayMs: 1000);

await retry.ExecuteAsync(
    () => tcpClient.ConnectAsync(endpoint),
    "TCP Connect"
);
```

### Error Handling
```csharp
try
{
    await operation();
}
catch (NetworkException ex)
{
    logger.LogError(ex, "Network operation failed");
    syncTracker.RecordError(ex.Message, peerNodeId, ex.ErrorCode);
}
catch (PersistenceException ex) when (ex is DatabaseCorruptionException)
{
    logger.LogCritical(ex, "Database corruption detected!");
    // Attempt recovery or alert admin
}
```

## Error Codes

| Code | Exception | Description |
|------|-----------|-------------|
| NETWORK_ERROR | NetworkException | Network operation failed |
| PERSISTENCE_ERROR | PersistenceException | Database operation failed |
| SYNC_ERROR | SyncException | Synchronization failed |
| CONFIG_ERROR | ConfigurationException | Invalid configuration |
| TIMEOUT_ERROR | TimeoutException | Operation timed out |

## Logging Levels

- **Trace**: Internal details (cache hits/misses)
- **Debug**: Debugging info (sync operations)
- **Information**: Normal events (peer discovered, backup created)
- **Warning**: Recoverable errors (queue full, retry attempt)
- **Error**: Failures requiring attention (sync failed, corruption detected)
- **Critical**: System failures (database initialization failed)

## Best Practices

1. **Always use structured logging**
   ```csharp
   _logger.LogInformation("User {UserId} synced {Count} documents", userId, count);
   ```

2. **Wrap network operations with retry policy**
   ```csharp
   await _retryPolicy.ExecuteAsync(() => client.ConnectAsync(), "Connect");
   ```

3. **Check cache before database**
   ```csharp
   var doc = _cache.Get(collection, key) ?? await _store.GetDocumentAsync(collection, key);
   ```

4. **Enable offline queue for LAN instability**
   ```csharp
   if (options.Sync.EnableOfflineQueue && !isOnline)
       _offlineQueue.Enqueue(operation);
   ```

5. **Periodic health checks**
   ```csharp
   var timer = new Timer(async _ => 
   {
       var health = await _healthCheck.CheckAsync();
       if (!health.IsHealthy)
           _logger.LogWarning("Health check failed: {Errors}", health.Errors);
   }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
   ```

## Deployment Checklist

- [ ] Configuration file created (appsettings.json)
- [ ] Log directory permissions set
- [ ] Backup directory configured
- [ ] Database file location specified
- [ ] Network ports configured (firewall)
- [ ] Health check endpoint tested
- [ ] Offline queue tested
- [ ] Backup/restore tested
- [ ] Graceful shutdown tested

## Troubleshooting

### Database corruption
```csharp
try
{
    await store.CheckIntegrityAsync();
}
catch (DatabaseCorruptionException)
{
    // Restore from backup
    File.Copy("backups/latest.db", options.Persistence.DatabasePath, overwrite: true);
}
```

### Network issues
```
Check sync tracker:
- Last sync time
- Active peers
- Recent errors
```

### Performance degradation
```csharp
var stats = cache.GetStatistics();
if (stats.HitRate < 0.5)
{
    // Consider increasing cache size
    options.Persistence.CacheSizeMb = 100;
}
```
