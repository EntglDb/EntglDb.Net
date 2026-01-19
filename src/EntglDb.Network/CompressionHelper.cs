using System;
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
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            brotli.Write(data, 0, data.Length);
        }
        return output.ToArray();
#else
        return data;
#endif
    }

    public static byte[] Decompress(byte[] compressedData)
    {
#if NET6_0_OR_GREATER
        using var input = new MemoryStream(compressedData);
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
        {
            brotli.CopyTo(output);
        }
        return output.ToArray();
#else
        throw new NotSupportedException("Brotli decompression not supported on this platform.");
#endif
    }
}
