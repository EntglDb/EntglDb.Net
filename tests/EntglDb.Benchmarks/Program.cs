using BenchmarkDotNet.Running;

namespace EntglDb.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<PeerStoreBenchmarks>();
    }
}
