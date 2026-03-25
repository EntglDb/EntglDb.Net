namespace EntglDb.Sync;

public class SnapshotRequiredException : Exception
{
    public SnapshotRequiredException() : base("Peer requires a full snapshot sync.") { }
}

