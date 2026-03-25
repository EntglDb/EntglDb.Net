# EntglDb.Services.FileTransfer

Peer-to-peer file transfer service for the **EntglDb** mesh network.  
Allows any node to query metadata and stream file downloads from remote peers over the shared TCP connection, with automatic SHA-256 integrity verification.

## What's Included

- **`IFileProvider`** — server-side abstraction you implement to control which files are exposed (prevents path traversal)
- **`FileQueryHandler`** — responds to file-metadata queries (wire type 1100)
- **`FileDownloadHandler`** — streams file chunks to the requesting peer (wire type 1102), with SHA-256 hash computed on the fly
- **`IFileTransferService`** — client-side API with `QueryAsync` and `DownloadAsync`
- **`FileTransferIntegrityException`** — thrown if the received data does not match the server's checksum

## Installation

```bash
dotnet add package EntglDb.Core
dotnet add package EntglDb.Network
dotnet add package EntglDb.Services.FileTransfer
```

## Quick Start

### 1 — Implement IFileProvider (server side)

```csharp
public class ReportFileProvider : IFileProvider
{
    private static readonly Dictionary<string, string> _catalogue = new()
    {
        ["report-q1"] = "/data/reports/q1-2026.pdf",
        ["report-q2"] = "/data/reports/q2-2026.pdf",
    };

    public Task<FileTransferInfo?> GetInfoAsync(string fileId, CancellationToken ct)
    {
        if (!_catalogue.TryGetValue(fileId, out var path) || !File.Exists(path))
            return Task.FromResult<FileTransferInfo?>(null);

        var fi = new System.IO.FileInfo(path);
        return Task.FromResult<FileTransferInfo?>(new FileTransferInfo
        {
            FileId    = fileId,
            FileName  = fi.Name,
            SizeBytes = fi.Length,
        });
    }

    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken ct)
    {
        if (!_catalogue.TryGetValue(fileId, out var path) || !File.Exists(path))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(path));
    }
}
```

### 2 — Register services

```csharp
builder.Services
    .AddEntglDbNetwork<MyConfigProvider>()
    .AddEntglDbFileTransfer();                    // registers handlers + IFileTransferService

builder.Services.AddSingleton<IFileProvider, ReportFileProvider>();
```

### 3 — Download a file from a peer

```csharp
public class MyService
{
    private readonly IFileTransferService _fileTransfer;
    public MyService(IFileTransferService fileTransfer) => _fileTransfer = fileTransfer;

    public async Task DownloadReportAsync(string peerAddress, CancellationToken ct)
    {
        // Check it exists first
        var info = await _fileTransfer.QueryAsync(peerAddress, "report-q1", ct);
        if (info is null) { Console.WriteLine("File not found on peer."); return; }

        Console.WriteLine($"Downloading {info.FileName} ({info.SizeBytes:N0} bytes)...");

        using var fs = File.OpenWrite("report-q1.pdf");
        await _fileTransfer.DownloadAsync(peerAddress, "report-q1", fs, ct);

        Console.WriteLine("Done. SHA-256 verified.");
    }
}
```

## Wire Protocol

| Type | Value | Direction | Description |
|------|-------|-----------|-------------|
| `FileQueryReq`        | 1100 | client → server | Ask if file exists |
| `FileQueryRes`        | 1101 | server → client | Metadata or not-found |
| `FileDownloadReq`     | 1102 | client → server | Start download |
| `FileChunkMsg`        | 1103 | server → client | Binary chunk (repeating) |
| `FileTransferDoneMsg` | 1104 | server → client | End-of-stream + SHA-256 |

## Documentation

- [Full README](https://github.com/EntglDb/EntglDb.Net#readme)
- [Custom P2P Services](https://github.com/EntglDb/EntglDb.Net#custom-p2p-services)
- [Message Type Ranges](https://github.com/EntglDb/EntglDb.Net#message-type-ranges)
