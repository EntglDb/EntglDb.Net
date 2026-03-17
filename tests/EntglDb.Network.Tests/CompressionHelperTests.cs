using System;
using System.Text;
using EntglDb.Network;
using Xunit;

namespace EntglDb.Network.Tests;

/// <summary>
/// Tests for CompressionHelper — strict round-trip, threshold behaviour,
/// and correctness across payload sizes including the decompressor's 4x fallback path.
/// </summary>
public class CompressionHelperTests
{
    /// <summary>Creates a byte array with a repeating-byte pattern (highly compressible).</summary>
    private static byte[] RepetitivePayload(int size)
    {
        var data = new byte[size];
        for (int i = 0; i < size; i++) data[i] = (byte)(i % 64);
        return data;
    }

    /// <summary>Creates a pseudo-random byte array (less compressible).</summary>
    private static byte[] RandomPayload(int size, int seed = 42)
    {
        var rng = new Random(seed);
        var data = new byte[size];
        rng.NextBytes(data);
        return data;
    }

    // ── Below-threshold pass-through ─────────────────────────────────────────

    [Fact]
    public void Compress_BelowThreshold_ReturnsSameReference()
    {
        byte[] small = RepetitivePayload(CompressionHelper.THRESHOLD - 1);

        byte[] result = CompressionHelper.Compress(small);

        // Must be the exact same array object — no copy, no compression attempted.
        Assert.Same(small, result);
    }

    [Fact]
    public void Compress_ZeroBytePayload_ReturnsSameReference()
    {
        byte[] empty = Array.Empty<byte>();

        byte[] result = CompressionHelper.Compress(empty);

        Assert.Same(empty, result);
    }

    // ── Compression reduces size ──────────────────────────────────────────────

    [Fact]
    public void Compress_AtThreshold_CompressesRepetitiveData()
    {
        byte[] data = RepetitivePayload(CompressionHelper.THRESHOLD);

        byte[] compressed = CompressionHelper.Compress(data);

        Assert.True(compressed.Length < data.Length,
            $"Expected compressed size < {data.Length} bytes but got {compressed.Length}");
    }

    [Fact]
    public void Compress_AllZeroBytes_ProducesVerySmallOutput()
    {
        byte[] data = new byte[8192]; // all zeros — maximally compressible

        byte[] compressed = CompressionHelper.Compress(data);

        Assert.True(compressed.Length < data.Length / 2,
            $"Expected < {data.Length / 2} bytes but got {compressed.Length}");
    }

    // ── Round-trip: Compress then Decompress ──────────────────────────────────

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void CompressDecompress_RepetitivePayload_RoundTrips(int size)
    {
        byte[] original = RepetitivePayload(size);

        byte[] decompressed = CompressionHelper.Decompress(CompressionHelper.Compress(original));

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressDecompress_BinaryContent_RoundTrips()
    {
        // Random bytes are harder to compress but the round-trip must still be lossless.
        byte[] original = RandomPayload(4096, seed: 7);

        byte[] decompressed = CompressionHelper.Decompress(CompressionHelper.Compress(original));

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressDecompress_Utf8Text_RoundTrips()
    {
        string text = string.Concat(System.Linq.Enumerable.Repeat("Hello, EntglDb! Distributed sync rocks. ", 100));
        byte[] original = Encoding.UTF8.GetBytes(text);
        Assert.True(original.Length >= CompressionHelper.THRESHOLD,
            "Payload must be above threshold so compression actually runs.");

        byte[] decompressed = CompressionHelper.Decompress(CompressionHelper.Compress(original));

        Assert.Equal(original, decompressed);
        Assert.Equal(text, Encoding.UTF8.GetString(decompressed));
    }

    [Fact]
    public void CompressDecompress_LargePayload_RoundTrips()
    {
        // 200 KB — the decompressor's initial 4× estimate (800 KB rented) will suffice.
        byte[] original = RepetitivePayload(200_000);

        byte[] decompressed = CompressionHelper.Decompress(CompressionHelper.Compress(original));

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressDecompress_LargeRandomPayload_RoundTrips()
    {
        // 100 KB random: compressed output ≈ input size; decompressor estimate still valid.
        byte[] original = RandomPayload(100_000, seed: 99);

        byte[] decompressed = CompressionHelper.Decompress(CompressionHelper.Compress(original));

        Assert.Equal(original, decompressed);
    }

    // ── Idempotency guard ─────────────────────────────────────────────────────

    [Fact]
    public void Compress_IdempotentResults_SameInputSameOutput()
    {
        byte[] data = RepetitivePayload(2048);

        byte[] c1 = CompressionHelper.Compress(data);
        byte[] c2 = CompressionHelper.Compress(data);

        // Two calls on the same data must produce byte-identical output.
        Assert.Equal(c1, c2);
    }

    // ── Output length invariants ──────────────────────────────────────────────

    [Fact]
    public void Compress_Result_HasNonZeroLength()
    {
        byte[] data = RepetitivePayload(CompressionHelper.THRESHOLD);

        byte[] compressed = CompressionHelper.Compress(data);

        Assert.True(compressed.Length > 0);
    }

    [Fact]
    public void Decompress_Result_HasCorrectLength()
    {
        byte[] original = RepetitivePayload(4096);
        byte[] compressed = CompressionHelper.Compress(original);

        byte[] decompressed = CompressionHelper.Decompress(compressed);

        Assert.Equal(original.Length, decompressed.Length);
    }
}
