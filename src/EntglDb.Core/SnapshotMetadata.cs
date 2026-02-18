namespace EntglDb.Core;

public class SnapshotMetadata
{
    public string NodeId { get; set; } = "";
    public long TimestampPhysicalTime { get; set; }
    public int TimestampLogicalCounter { get; set; }
    public string Hash { get; set; } = "";
}
