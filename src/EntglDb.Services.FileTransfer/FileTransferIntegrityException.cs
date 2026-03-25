using System;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Thrown by <see cref="IFileTransferService.DownloadAsync"/> when the SHA-256 checksum
/// of the received data does not match the checksum reported by the remote peer.
/// </summary>
public sealed class FileTransferIntegrityException : Exception
{
    /// <summary>The SHA-256 checksum the remote peer claimed.</summary>
    public string Expected { get; }

    /// <summary>The SHA-256 checksum of the data actually received.</summary>
    public string Actual { get; }

    internal FileTransferIntegrityException(string fileId, string expected, string actual)
        : base($"Integrity check failed for file '{fileId}': expected SHA-256 {expected}, got {actual}.")
    {
        Expected = expected;
        Actual = actual;
    }
}
