using System;
using System.IO;
using System.Security.Cryptography;

namespace EntglDb.Network.Security;

public static class CryptoHelper
{
    private const int KeySize = 32; // 256 bits
    private const int BlockSize = 16; // 128 bits
    private const int MacSize = 32; // 256 bits (HMACSHA256)

    public static (byte[] ciphertext, byte[] iv, byte[] tag) Encrypt(byte[] plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // Compute HMAC
        using var hmac = new HMACSHA256(key);
        // Authenticate IV + Ciphertext
        var toSign = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, toSign, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, toSign, iv.Length, ciphertext.Length);
        var tag = hmac.ComputeHash(toSign);

        return (ciphertext, iv, tag);
    }

    public static byte[] Decrypt(byte[] ciphertext, byte[] iv, byte[] tag, byte[] key)
    {
        // Verify HMAC
        using var hmac = new HMACSHA256(key);
        var toVerify = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, toVerify, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, toVerify, iv.Length, ciphertext.Length);
        var computedTag = hmac.ComputeHash(toVerify);

        if (!FixedTimeEquals(tag, computedTag))
        {
            throw new CryptographicException("Authentication failed (HMAC mismatch)");
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
#if NET6_0_OR_GREATER
        return CryptographicOperations.FixedTimeEquals(left, right);
#else
        if (left.Length != right.Length) return false;
        int res = 0;
        for (int i = 0; i < left.Length; i++) res |= left[i] ^ right[i];
        return res == 0;
#endif
    }
}
