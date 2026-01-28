# Network Security

**EntglDb v0.6.0** introduces optional **secure networking** to protect data in transit between peers using industry-standard cryptography.

## Overview

The security layer provides:
- **ECDH (Elliptic Curve Diffie-Hellman)** key exchange for establishing shared secrets
- **AES-256-CBC** encryption for all synchronized data
- **HMAC-SHA256** authentication to prevent tampering
- **Perfect Forward Secrecy** - each session uses unique ephemeral keys

## When to Use Secure Networking

### ✅ Recommended For:
- **Sensitive data** (customer information, financial records, health data)
- **Compliance requirements** (GDPR, HIPAA, PCI-DSS)
- **Untrusted network segments** within your LAN
- **Production deployments** where data confidentiality is important

### ⚠️ Consider Performance Impact:
- Encryption adds ~5-10ms latency per message
- CPU overhead for encryption/decryption
- Not necessary for public/non-sensitive data

## How It Works

### Handshake Process

```
Peer A                               Peer B
  |                                     |
  |  1. Generate ECDH keypair           |
  |------ Public Key A ---------------->|  2. Generate ECDH keypair
  |                                     |
  |<----- Public Key B -----------------|
  |                                     |
  3. Compute shared secret           3. Compute shared secret
  4. Derive encryption keys          4. Derive encryption keys
  |                                 |
  |==== Encrypted Communication ====|
```

### Encryption Details

1. **Key Exchange**: NIST P-256 elliptic curve (secp256r1)
2. **Encryption**: AES-256 in CBC mode with random IV per message
3. **Authentication**: HMAC-SHA256 over ciphertext
4. **Message Format**: `IV (16 bytes) + Ciphertext + HMAC (32 bytes)`

## Usage

### Enable Security

```csharp
using EntglDb.Network.Security;

// Register secure handshake service
services.AddSingleton<IPeerHandshakeService, SecureHandshakeService>();

// Network will automatically use encryption if service is registered
services.AddEntglDbNetwork(nodeId, tcpPort, authToken);
```

### Console Sample (--secure flag)

```bash
# Start with encryption enabled
dotnet run --secure

# Or without encryption (faster, less secure)
dotnet run
```

### UI Samples

Avalonia and MAUI samples have **always-on security** by default. No configuration needed.

### Verify Security Status

All UI samples display security status:
- **🔒 Encrypted** - Secure communication active
- **🔓 Plaintext** - No encryption (Console sample default)

## Configuration

### Optional: Custom Security Settings

Currently, security uses default secure parameters. Future versions may support:
- Custom curve selection
- Cipher suite configuration  
- Certificate pinning

## Security Considerations

### ✅ What This Protects Against:
- **Eavesdropping**: Data is encrypted in transit
- **Tampering**: HMAC prevents message modification
- **Man-in-the-Middle**: ECDH provides authenticity

### ⚠️ Out of Scope:
- **Data at Rest**: SQLite databases are NOT encrypted. Use OS-level encryption if needed.
- **Authentication**: No peer identity verification beyond shared auth token.
- **Public Internet**: Still designed for trusted LANs. Use VPN/firewall for internet exposure.

## Performance Impact

Benchmarks on typical workloads:

| Operation | Plaintext | Encrypted | Overhead |
|-----------|-----------|-----------|----------|
| Small sync (10 docs) | 15ms | 22ms | +47% |
| Large sync (1000 docs) | 450ms | 520ms | +16% |
| Handshake | N/A | 8ms | Initial |

**Recommendation**: Enable for production. Disable for development/testing if needed.

## Compatibility

- **Secure ↔ Secure**: ✅ Works
- **Plaintext ↔ Plaintext**: ✅ Works  
- **Secure ↔ Plaintext**: ❌ Connection fails (by design)

All nodes in a network must use the **same security mode**.

## FAQ

**Q: Can I use this over the public internet?**  
A: While encryption helps, EntglDb is still designed for LANs. Use VPN, firewall rules, and consider TLS/SSL wrapping for internet exposure.

**Q: How do I encrypt the SQLite database?**  
A: Use SQLCipher or OS-level disk encryption (BitLocker, LUKS, FileVault).

**Q: Can different nodes use different keys?**  
A: No. All nodes in a mesh share the same `authToken`. Key rotation is not yet supported.

**Q: What about .NET Standard 2.0 support?**  
A: Security works on all target frameworks (netstandard2.0, net6.0, net8.0) with appropriate polyfills.

---

**See Also:**
- [Getting Started](getting-started.md)
- [Architecture](architecture.md)
- [Production Hardening](production-hardening.md)
