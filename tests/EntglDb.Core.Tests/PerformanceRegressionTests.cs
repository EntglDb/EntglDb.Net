using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EntglDb.Core.Sync;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EntglDb.Core.Tests.Sync
{
    public class PerformanceRegressionTests
    {
        private readonly ITestOutputHelper _output;
        private readonly RecursiveNodeMergeConflictResolver _resolver;
        private readonly Dictionary<string, int> _limits;

        public PerformanceRegressionTests(ITestOutputHelper output)
        {
            _output = output;
            _resolver = new RecursiveNodeMergeConflictResolver();
            
            // Load limits
            var json = File.ReadAllText("benchmark_limits.json");
            _limits = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        }

        private Document CreateDoc(string key, object data, HlcTimestamp ts)
        {
            var json = JsonSerializer.Serialize(data);
            var element = JsonDocument.Parse(json).RootElement;
            return new Document("test", key, element, ts, false);
        }

        private OplogEntry CreateOp(string key, object data, HlcTimestamp ts)
        {
            var json = JsonSerializer.Serialize(data);
            var element = JsonDocument.Parse(json).RootElement;
            return new OplogEntry("test", key, OperationType.Put, element, ts);
        }

        [Fact]
        public void RecursiveMerge_Simple_ShouldBeWithinLimits()
        {
            int iterations = 10000;
            string limitKey = "RecursiveMerge_Simple_10k_Ops_MaxMs";

            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");
            var doc = CreateDoc("k1", new { name = "Alice", age = 30 }, ts1);
            var op = CreateOp("k1", new { name = "Bob", age = 31 }, ts2);

            // Warmup
            for (int i = 0; i < 100; i++) _resolver.Resolve(doc, op);

            // Run
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _resolver.Resolve(doc, op);
            }
            sw.Stop();

            long elapsed = sw.ElapsedMilliseconds;
            _output.WriteLine($"Executed {iterations} merges in {elapsed}ms");

            if (_limits.TryGetValue(limitKey, out int maxMs))
            {
                elapsed.Should().BeLessThan(maxMs, $"Performance regression! Expected < {maxMs}ms but took {elapsed}ms");
            }
            else
            {
                _output.WriteLine($"Warning: No limit found for key '{limitKey}'");
            }
        }

        [Fact]
        public void RecursiveMerge_DeepArray_ShouldBeWithinLimits()
        {
            int iterations = 1000; // Lower iterations for heavier op
            string limitKey = "RecursiveMerge_Array_1k_Ops_MaxMs";

            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");

            var items = new List<object>();
            for(int i=0; i<100; i++) items.Add(new { id = i.ToString(), val = i });
            
            var doc = CreateDoc("k1", new { items = items }, ts1);
            var op = CreateDoc("k1", new { items = items }, ts2).ToOplogEntry(OperationType.Put); // Same content to force id check traversal

            // Warmup
            _resolver.Resolve(doc, op);

             // Run
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _resolver.Resolve(doc, op);
            }
            sw.Stop();

            long elapsed = sw.ElapsedMilliseconds;
            _output.WriteLine($"Executed {iterations} array merges in {elapsed}ms");

            if (_limits.TryGetValue(limitKey, out int maxMs))
            {
                elapsed.Should().BeLessThan(maxMs, $"Performance regression! Expected < {maxMs}ms but took {elapsed}ms");
            }
        }
    }

    public static class DocExt {
        public static OplogEntry ToOplogEntry(this Document d, OperationType t) {
            return new OplogEntry(d.Collection, d.Key, t, d.Content, d.UpdatedAt);
        }
    }
}
