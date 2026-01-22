using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network.Telemetry;

public class NetworkTelemetryService : INetworkTelemetryService, IDisposable
{
    private readonly Channel<(MetricType Type, double Value)> _metricChannel;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<NetworkTelemetryService> _logger;
    private readonly string _persistencePath;
    
    // Aggregation State
    // We keep 30m of history with 1s resolution = 1800 buckets.
    private const int MaxHistorySeconds = 1800;
        
    private readonly object _lock = new object();
    private readonly MetricBucket[] _history;
    private int _headIndex = 0; // Points to current second
    private long _currentSecondTimestamp; // Unix timestamp of current bucket

    // Rolling Averages (Last calculated)
    private readonly Dictionary<string, double> _averages = new Dictionary<string, double>();

    public NetworkTelemetryService(ILogger<NetworkTelemetryService> logger, string persistencePath)
    {
        _logger = logger;
        _persistencePath = persistencePath;
        _metricChannel = Channel.CreateUnbounded<(MetricType, double)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false 
        });
        _cts = new CancellationTokenSource();
        
        _history = new MetricBucket[MaxHistorySeconds];
        for (int i = 0; i < MaxHistorySeconds; i++) _history[i] = new MetricBucket();

        _currentSecondTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        _ = Task.Run(ProcessMetricsLoop);
        _ = Task.Run(PersistenceLoop);
    }

    public void RecordValue(MetricType type, double value)
    {
        _metricChannel.Writer.TryWrite((type, value));
    }

    public MetricTimer StartMetric(MetricType type)
    {
        return new MetricTimer(this, type);
    }

    public Dictionary<MetricType, Dictionary<int, double>> GetSnapshot()
    {
        var snapshot = new Dictionary<MetricType, Dictionary<int, double>>();
        var windows = new[] { 60, 300, 600, 1800 }; 

        lock (_lock)
        {
            foreach (var type in Enum.GetValues(typeof(MetricType)).Cast<MetricType>())
            {
                var typeDict = new Dictionary<int, double>();
                foreach (var w in windows)
                {
                    typeDict[w] = CalculateAverage(type, w);
                }
                snapshot[type] = typeDict;
            }
        }
        return snapshot;
    }

    private async Task ProcessMetricsLoop()
    {
        var reader = _metricChannel.Reader;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (await reader.WaitToReadAsync(_cts.Token))
                {
                    while (reader.TryRead(out var item))
                    {
                        AddMetricToCurrentBucket(item.Type, item.Value);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing metrics");
            }
        }
    }

    private void AddMetricToCurrentBucket(MetricType type, double value)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        lock (_lock)
        {
            // Rotate bucket if second changed
            if (now > _currentSecondTimestamp)
            {
                long diff = now - _currentSecondTimestamp;
                // Move head forward, clearing buckets in between if gap > 1s
                for (int i = 0; i < diff && i < MaxHistorySeconds; i++)
                {
                    _headIndex = (_headIndex + 1) % MaxHistorySeconds;
                    _history[_headIndex].Reset();
                }
                _currentSecondTimestamp = now;
            }

            _history[_headIndex].Add(type, value);
        }
    }

    private async Task PersistenceLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token);
                CalculateAndPersist();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting metrics");
            }
        }
    }

    private void CalculateAndPersist()
    {
        lock (_lock)
        {
            // Calculate averages
            var windows = new[] { 60, 300, 600, 1800 }; // 1m, 5m, 10m, 30m
            
            using var fs = new FileStream(_persistencePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            
            // Header
            bw.Write((byte)1); // Version
            bw.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // Timestamp
            
            foreach (var type in Enum.GetValues(typeof(MetricType)).Cast<MetricType>())
            {
                bw.Write((int)type);
                foreach (var w in windows)
                {
                    double avg = CalculateAverage(type, w);
                    bw.Write(w);     // Window Seconds
                    bw.Write(avg);   // Average Value
                }
            }
        }
    }

    internal void ForcePersist()
    {
        CalculateAndPersist();
    }

    private double CalculateAverage(MetricType type, int seconds)
    {
        // Go backwards from head
        double sum = 0;
        int count = 0;
        int scanned = 0;
        
        int idx = _headIndex;
        
        while (scanned < seconds && scanned < MaxHistorySeconds)
        {
            var bucket = _history[idx];
            sum += bucket.GetSum(type);
            count += bucket.GetCount(type);
            
            idx--;
            if (idx < 0) idx = MaxHistorySeconds - 1;
            scanned++;
        }
        
        return count == 0 ? 0 : sum / count;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

internal class MetricBucket
{
    // Simple lock-free or locked accumulation? Global lock handles it for now.
    // Storing Sum and Count for each type
    private readonly double[] _sums;
    private readonly int[] _counts;

    public MetricBucket()
    {
        var typeCount = Enum.GetValues(typeof(MetricType)).Length;
        _sums = new double[typeCount];
        _counts = new int[typeCount];
    }

    public void Reset()
    {
        Array.Clear(_sums, 0, _sums.Length);
        Array.Clear(_counts, 0, _counts.Length);
    }

    public void Add(MetricType type, double value)
    {
        int idx = (int)type;
        _sums[idx] += value;
        _counts[idx]++;
    }

    public double GetSum(MetricType type) => _sums[(int)type];
    public int GetCount(MetricType type) => _counts[(int)type];
}
