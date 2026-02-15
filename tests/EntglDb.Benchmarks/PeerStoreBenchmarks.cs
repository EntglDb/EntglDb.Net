using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Blite;
using EntglDb.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;


namespace EntglDb.Benchmarks
{
    [SimpleJob]
    [MemoryDiagnoser]
    public class PeerStoreBenchmarks
    {
        private IPeerStore _bliteStore = null!;
        private IPeerStore _sqliteStore = null!;
        private string _blitePath = null!;
        private string _sqlitePath = null!;
        private Document[] _documents = null!;

        [Params(100, 1000)]
        public int N;

        [GlobalSetup]
        public async Task Setup()
        {
            // BlitePeerStore expects a file path, not a directory
            var bliteDir = Path.Combine(Path.GetTempPath(), "blite_bench_" + Guid.NewGuid());
            _blitePath = Path.Combine(bliteDir, "bench.db");
            _sqlitePath = Path.Combine(Path.GetTempPath(), "sqlite_bench_" + Guid.NewGuid() + ".db");

            // Setup BLite
            Directory.CreateDirectory(bliteDir);
            _bliteStore = new BlitePeerStore(_blitePath);

            // Setup SQLite
            _sqliteStore = new SqlitePeerStore($"Data Source={_sqlitePath}");

            // Prepare data
            _documents = Enumerable.Range(0, N).Select(i => 
                new Document("bench_col", $"key_{i}", 
                    System.Text.Json.JsonDocument.Parse($"{{\"id\": {i}, \"name\": \"Valid Name {i}\", \"value\": {i * 10}}}").RootElement, 
                    new HlcTimestamp(DateTime.UtcNow.Ticks, 0, "node1"), 
                    false)
            ).ToArray();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            (_bliteStore as IDisposable)?.Dispose();
            // (_sqliteStore as IDisposable)?.Dispose();

            // Delete BLite database file and directory
            if (File.Exists(_blitePath)) File.Delete(_blitePath);
            var bliteDir = Path.GetDirectoryName(_blitePath);
            if (!string.IsNullOrEmpty(bliteDir) && Directory.Exists(bliteDir)) 
                Directory.Delete(bliteDir, true);
            
            // Clear SQLite connection pool before deleting the file
            SqliteConnection.ClearAllPools();
            
            // Delete SQLite database file
            if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath);
        }

        [Benchmark]
        public async Task BLite_Insert()
        {
            foreach (var doc in _documents)
            {
                await _bliteStore.SaveDocumentAsync(doc);
            }
        }

        [Benchmark]
        public async Task SQLite_Insert()
        {
            foreach (var doc in _documents)
            {
                await _sqliteStore.SaveDocumentAsync(doc);
            }
        }

        // Batch insert using ApplyBatchAsync (simulating sync)
        [Benchmark]
        public async Task BLite_InsertBatch()
        {
            await _bliteStore.ApplyBatchAsync(_documents, Enumerable.Empty<OplogEntry>());
        }

        [Benchmark]
        public async Task SQLite_InsertBatch()
        {
            await _sqliteStore.ApplyBatchAsync(_documents, Enumerable.Empty<OplogEntry>());
        }
    }
}
