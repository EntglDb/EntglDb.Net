using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network.Security
{
    public class SecureHandshakeService : IPeerHandshakeService
    {
        // Simple protocol:
        // Initiator -> [Public Key Length (4) + Public Key]
        // Responder -> [Public Key Length (4) + Public Key]
        // Both derive shared secret -> Split into SendKey/RecvKey using HKDF
        
        public async Task<CipherState?> HandshakeAsync(Stream stream, bool isInitiator, string myNodeId, CancellationToken token)
        {
#if NET6_0_OR_GREATER
            using var ecdh = ECDiffieHellman.Create();
            ecdh.KeySize = 256;

            // 1. Export & Send Public Key
            var myPublicKey = ecdh.ExportSubjectPublicKeyInfo();
            var lenBytes = BitConverter.GetBytes(myPublicKey.Length);
            await stream.WriteAsync(lenBytes, 0, 4, token);
            await stream.WriteAsync(myPublicKey, 0, myPublicKey.Length, token);

            // 2. Receive Peer Public Key
            var peerLenBuf = new byte[4];
            await ReadExactAsync(stream, peerLenBuf, 0, 4, token);
            int peerLen = BitConverter.ToInt32(peerLenBuf, 0);

            var peerKeyBytes = new byte[peerLen];
            await ReadExactAsync(stream, peerKeyBytes, 0, peerLen, token);

            // 3. Import Peer Key & Derive Shared Secret
            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(peerKeyBytes, out _);
            
            byte[] sharedSecret = ecdh.DeriveKeyMaterial(peerEcdh.PublicKey);

            // 4. Derive Session Keys (HKDF-like expansion)
            // Use SHA256 to split/expand secret into EncryptKey and DecryptKey
            // Simple approach: Hash(secret + "0") -> Key1, Hash(secret + "1") -> Key2
            
            using var sha = SHA256.Create();
            
            var k1Input = new byte[sharedSecret.Length + 1];
            Buffer.BlockCopy(sharedSecret, 0, k1Input, 0, sharedSecret.Length);
            k1Input[sharedSecret.Length] = 0; // "0"
            var key1 = sha.ComputeHash(k1Input);
            
            var k2Input = new byte[sharedSecret.Length + 1];
            Buffer.BlockCopy(sharedSecret, 0, k2Input, 0, sharedSecret.Length);
            k2Input[sharedSecret.Length] = 1; // "1"
            var key2 = sha.ComputeHash(k2Input);

            // If initiator: Encrypt with Key1, Decrypt with Key2
            // If responder: Encrypt with Key2, Decrypt with Key1
            
            var encryptKey = isInitiator ? key1 : key2;
            var decryptKey = isInitiator ? key2 : key1;

            return new CipherState(encryptKey, decryptKey);
#else
            // For netstandard2.0, standard ECDH import is broken/hard without external libs.
            // Returning null or throwing.
            throw new PlatformNotSupportedException("Secure handshake requires .NET 6.0+");
#endif
        }

        private async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total, token);
                if (read == 0) throw new EndOfStreamException();
                total += read;
            }
            return total;
        }
    }
}
