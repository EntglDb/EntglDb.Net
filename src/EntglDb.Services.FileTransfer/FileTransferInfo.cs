namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Metadata about a file available on a remote peer.
/// </summary>
public sealed class FileTransferInfo
{
    /// <summary>Opaque identifier used to request the file via <see cref="IFileTransferService"/>.</summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the file (no path components).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Total size of the file in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 checksum pre-computed by the server, or <c>null</c>
    /// if the provider does not supply one upfront.
    /// The <em>download</em> path always verifies integrity regardless of this value.
    /// </summary>
    public string? Sha256Checksum { get; set; }
}
