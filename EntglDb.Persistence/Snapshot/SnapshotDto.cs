using System.Collections.Generic;

namespace EntglDb.Persistence.Snapshot;

/// <summary>
/// Root DTO for EF Core snapshots (JSON format).
/// </summary>
public class SnapshotDto
{
    public string Version { get; set; } = "1.0";
    public string CreatedAt { get; set; } = "";
    public string NodeId { get; set; } = "";
    public List<DocumentDto> Documents { get; set; } = new();
    public List<OplogDto> Oplog { get; set; } = new();
    public List<SnapshotMetadataDto> SnapshotMetadata { get; set; } = new();
    public List<RemotePeerDto> RemotePeers { get; set; } = new();
}

public class DocumentDto
{
    public string Collection { get; set; } = "";
    public string Key { get; set; } = "";
    public string? JsonData { get; set; }
    public bool IsDeleted { get; set; }
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string HlcNode { get; set; } = "";
}

public class OplogDto
{
    public string? Collection { get; set; }
    public string? Key { get; set; }
    public int Operation { get; set; }
    public string? JsonData { get; set; }
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string HlcNode { get; set; } = "";
    public string Hash { get; set; } = "";
    public string? PreviousHash { get; set; }
}

public class SnapshotMetadataDto
{
    public string NodeId { get; set; } = "";
    public long HlcWall { get; set; }
    public int HlcLogic { get; set; }
    public string Hash { get; set; } = "";
}

public class RemotePeerDto
{
    public string NodeId { get; set; } = "";
    public string Address { get; set; } = "";
    public int Type { get; set; }
    public string? OAuth2Json { get; set; }
    public bool IsEnabled { get; set; }
}
