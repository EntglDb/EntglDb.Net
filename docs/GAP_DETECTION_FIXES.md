# Gap Detection and Reconciliation Fixes

## Overview

This update addresses issues with gap detection and reconciliation in EntglDb's synchronization system.

## Changes

### 1. ApplyBatchAsync Validation

**Problem**: Put operations without payload could be written to the oplog without updating documents, causing oplog growth without actual data changes.

**Solution**: Both `SqlitePeerStore` and `EfCorePeerStore` now validate that Put operations include a payload:
- Logs a warning when a Put operation lacks payload
- Skips the oplog entry entirely (does not write to oplog)
- Preserves Last-Write-Wins conflict resolution behavior

**Code Example**:
```csharp
// In ApplyBatchAsync
if (entry.Operation == OperationType.Put && 
    (entry.Payload == null || entry.Payload.Value.ValueKind == JsonValueKind.Undefined))
{
    _logger.LogWarning("Rejecting Put operation without payload for {Collection}/{Key}", 
        entry.Collection, entry.Key);
    continue; // Skip this entry
}
```

### 2. Gap Detection Service

**Problem**: After restart, the system would re-request historical gaps because there was no persistent tracking of which sequences had already been received.

**Solution**: Implemented `GapDetectionService` with persistent state tracking:
- **`NodeSequenceTracker`**: Tracks highest contiguous timestamp received from each node
- **`GapDetectionService`**: Manages gap detection with persistent state seeding
- Seeds from persistent oplog data on first use
- Updates tracking after successful ApplyBatch
- Prevents repeated gap requests for already-synchronized data

**Usage Example**:
```csharp
// Create gap detection service
var gapDetection = new GapDetectionService(store);

// Seed from persistent state (call once on startup)
await gapDetection.EnsureSeededAsync();

// Check for gaps before requesting data
if (gapDetection.HasGap("node1", remoteTimestamp))
{
    // Request missing entries
    var missingEntries = await client.PullChangesAsync(localTimestamp);
    
    // Apply the batch
    await store.ApplyBatchAsync(documents, missingEntries);
    
    // Update gap tracking to prevent re-requesting
    gapDetection.UpdateAfterApplyBatch(missingEntries);
}
```

### 3. Architecture

The gap detection service is designed to be used at the orchestration layer (e.g., `SyncOrchestrator`) rather than within the storage layer:

```
SyncOrchestrator
    ├── GapDetectionService (tracks contiguous sequences)
    ├── TcpPeerClient (network communication)
    └── IPeerStore (data persistence)
        ├── SqlitePeerStore
        └── EfCorePeerStore
```

### 4. Testing

Comprehensive tests cover:
- ✅ Gap detection seeding from persistent state
- ✅ No repeated gap requests after restart
- ✅ Backfill updates contiguous state
- ✅ Multi-node sequence tracking
- ✅ ApplyBatch with empty documents + oplog entries
- ✅ ApplyBatch rejection of Put without payload

All tests pass for both SQLite and general behavior.

### 5. Health Check Verification

Confirmed that HealthCheck services remain properly layered:
- `EntglDb.Core.Diagnostics.EntglDbHealthCheck` - Core health check logic
- `EntglDb.AspNet.HealthChecks.EntglDbHealthCheck` - ASP.NET integration
- No mislayered or duplicated services introduced

## Impact

1. **Reduced Oplog Growth**: Invalid Put operations no longer bloat the oplog
2. **Eliminated Repeated Requests**: Gap detection prevents re-requesting already-synchronized data after restart
3. **Improved Performance**: Less network traffic and storage overhead
4. **Maintained Consistency**: Last-Write-Wins behavior preserved for conflict resolution

## Migration Notes

No breaking changes. The gap detection service is opt-in and can be integrated into existing sync orchestration code as needed.

## Future Enhancements

Potential improvements for consideration:
- Persistent storage of gap detection state in a dedicated table
- Background task to periodically persist sequence tracking
- Metrics/telemetry for gap detection performance
- Configuration options for gap detection behavior
