using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace EntglDb.Network;

public static class CompressionHelper
{
    public const int THRESHOLD = 1024; // 1KB

    public static bool IsBrotliSupported
    {
        get
        {
#if NET6_0_OR_GREATER
            return true;
#else
            return false;
#endif
        }
    }

    public static byte[] Compress(byte[] data)
    {
        if (data.Length < THRESHOLD || !IsBrotliSupported) return data;

#if NET6_0_OR_GREATER
        // Rent a buffer large enough for the compressed output.
        // Brotli worst-case is slightly above the source; data.Length + 1024 is a safe upper bound.
        int maxSize = data.Length + 1024;
        byte[] rented = ArrayPool<byte>.Shared.Rent(maxSize);
        try
        {
            if (BrotliEncoder.TryCompress(data, rented, out int bytesWritten, quality: 1, window: 22))
            {
                // Copy only the written bytes into a correctly-sized result array.
                return rented[..bytesWritten];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        // Fallback: MemoryStream path (should not normally be reached).
        using var output = new MemoryStream(data.Length);
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
            brotli.Write(data, 0, data.Length);
        return output.ToArray();
#else
        return data;
#endif
    }

    public static byte[] Decompress(byte[] compressedData)
    {
#if NET6_0_OR_GREATER
        // Attempt a single-shot decode into a pooled buffer.
        // Start at 4x the compressed size; fall back to MemoryStream if the estimate is too small.
        int estimatedSize = compressedData.Length * 4;
        byte[] rented = ArrayPool<byte>.Shared.Rent(estimatedSize);
        try
        {
            var decoder = new BrotliDecoder();
            OperationStatus status = decoder.Decompress(
                compressedData, rented, out _, out int written);

            if (status == OperationStatus.Done)
                return rented[..written];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        // Fallback: MemoryStream path for large payloads that exceed the 4x estimate.
        using var input = new MemoryStream(compressedData);
        using var output = new MemoryStream(compressedData.Length * 8);
        using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
            brotli.CopyTo(output);
        return output.ToArray();
#else
        throw new NotSupportedException("Brotli decompression not supported on this platform.");
#endif
    }
}
