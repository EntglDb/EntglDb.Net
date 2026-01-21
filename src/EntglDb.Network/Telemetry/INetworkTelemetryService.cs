using System;
using System.Diagnostics;

namespace EntglDb.Network.Telemetry;

public interface INetworkTelemetryService
{
    void RecordValue(MetricType type, double value);
    MetricTimer StartMetric(MetricType type);
    System.Collections.Generic.Dictionary<MetricType, System.Collections.Generic.Dictionary<int, double>> GetSnapshot();
}

public readonly struct MetricTimer : IDisposable
{
    private readonly INetworkTelemetryService _service;
    private readonly MetricType _type;
    private readonly long _startTimestamp;

    public MetricTimer(INetworkTelemetryService service, MetricType type)
    {
        _service = service;
        _type = type;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        var elapsed = Stopwatch.GetTimestamp() - _startTimestamp;
        // Convert ticks to milliseconds? Or keep as ticks? 
        // Plan said "latency", usually ms.
        // Stopwatch.Frequency depends on hardware.
        // Let's store MS representation.
        double ms = (double)elapsed * 1000 / Stopwatch.Frequency;
        _service.RecordValue(_type, ms);
    }
}
