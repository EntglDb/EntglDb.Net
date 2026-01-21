---
layout: default
title: Network Telemetry
nav_order: 9
---

# Network Telemetry

Starting from v0.7.2, EntglDb includes a built-in telemetry system to monitor network performance, compression efficiency, and encryption overhead. This system is designed to have near-zero impact on runtime performance while providing valuable insights into peer-to-peer communication.

## Collected Metrics

The system collects the following metrics:

| Metric Type | Description | Unit |
| :--- | :--- | :--- |
| **CompressionRatio** | Ratio of compressed size to original size. Lower is better (e.g., 0.4 means 60% reduction). | Ratio (0.0 - 1.0) |
| **EncryptionTime** | Time taken to encrypt a message payload. | Milliseconds (ms) |
| **DecryptionTime** | Time taken to decrypt a received message. | Milliseconds (ms) |
| **RoundTripTime** | Time taken for a `GetClock` request-response cycle (latency). | Milliseconds (ms) |

## Architecture

The telemetry service uses a high-performance, non-blocking architecture:
1.  **Capture**: Metrics are pushed to a `System.Threading.Channels` queue, ensuring the critical network path is never blocked.
2.  **Aggregation**: A background worker aggregates samples into **1-second buckets**.
3.  **Rolling Windows**: Averages are calculated on-the-fly for **1m, 5m, 10m, and 30m** windows.
4.  **Persistence**: Aggregated data is automatically persisted to a local binary file (`entgldb_metrics.bin`) every minute.

## Configuration

Telemetry is enabled by default when using the standard DI extensions.

### Dependency Injection

When using `AddEntglDbNetwork`, the `INetworkTelemetryService` is automatically registered as a singleton.

```csharp
services.AddEntglDbCore()
        .AddEntglDbNetwork<MyNodeConfiguration>();
```

Use `INetworkTelemetryService` to access metric data programmatically:

```csharp
public class MyService
{
    private readonly INetworkTelemetryService _telemetry;

    public MyService(INetworkTelemetryService telemetry)
    {
        _telemetry = telemetry;
    }

    public void PrintMetrics()
    {
        var snapshot = _telemetry.GetSnapshot();
        // Access snapshot[MetricType.CompressionRatio][60] for 1-minute average
    }
}
```

## Security & Privacy

- Telemetry data is **stored locally** (on the device/server).
- No data is sent to external servers or other peers.
- Metric data does not contain any PII or payload content.

## Viewing Metrics

### Mobile Dashboard (MAUI)
The sample MAUI application includes a **Telemetry Dashboard** that visualizes these metrics in real-time. Navigate to the **Telemetry** tab to see live performance data.
