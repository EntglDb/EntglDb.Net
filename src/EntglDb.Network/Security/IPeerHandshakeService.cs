using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network.Security;

public interface IPeerHandshakeService
{
    /// <summary>
    /// Performs a handshake to establishing identity and optional security context.
    /// </summary>
    /// <returns>A CipherState if encryption is established, or null if plaintext.</returns>
    Task<CipherState?> HandshakeAsync(System.IO.Stream stream, bool isInitiator, string myNodeId, CancellationToken token);
}

public class CipherState
{
    public byte[] EncryptKey { get; }
    public byte[] DecryptKey { get; }
    // For simplicity using IV chaining or explicit IVs. 
    // We'll store just the keys here and let the encryption helper handle IVs.
    
    public CipherState(byte[] encryptKey, byte[] decryptKey)
    {
        EncryptKey = encryptKey;
        DecryptKey = decryptKey;
    }
}
