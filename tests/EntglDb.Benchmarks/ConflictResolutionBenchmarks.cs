using System;
using System.Text.Json;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using EntglDb.Core;
using EntglDb.Core.Sync;

namespace EntglDb.Benchmarks;

[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
public class ConflictResolutionBenchmarks
{
    private RecursiveNodeMergeConflictResolver _resolver;
    
    private Document _docSimple;
    private OplogEntry _opSimple;
    
    private Document _docArray1000;
    private OplogEntry _opArray1000;

    [GlobalSetup]
    public void Setup()
    {
        _resolver = new RecursiveNodeMergeConflictResolver();
        var ts1 = new HlcTimestamp(100, 0, "n1");
        var ts2 = new HlcTimestamp(200, 0, "n2");

        // Simple Case
        _docSimple = new Document("c", "k1", JsonDocument.Parse("{\"name\":\"A\", \"val\":1}").RootElement, ts1, false);
        _opSimple = new OplogEntry("c", "k1", OperationType.Put, JsonDocument.Parse("{\"name\":\"A\", \"val\":2}").RootElement, ts2);

        // Large Array (Object Keyed)
        var items1 = new List<object>();
        var items2 = new List<object>();
        for(int i = 0; i < 1000; i++)
        {
            items1.Add(new { id = i.ToString(), val = i });
            // Modify half, add half
            items2.Add(new { id = i.ToString(), val = i + 1 });
        }
        // Add new ones
        for(int i=1000; i<1500; i++)
        {
            items2.Add(new { id = i.ToString(), val = i });
        }

        var json1 = JsonSerializer.Serialize(new { items = items1 });
        var json2 = JsonSerializer.Serialize(new { items = items2 });

        _docArray1000 = new Document("c", "k2", JsonDocument.Parse(json1).RootElement, ts1, false);
        _opArray1000 = new OplogEntry("c", "k2", OperationType.Put, JsonDocument.Parse(json2).RootElement, ts2);
    }

    [Benchmark]
    public void MergeSimple()
    {
        _resolver.Resolve(_docSimple, _opSimple);
    }

    [Benchmark]
    public void MergeArray1500_Keyed()
    {
        _resolver.Resolve(_docArray1000, _opArray1000);
    }
}
