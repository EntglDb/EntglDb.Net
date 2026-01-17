using System.Text.Json;
using EntglDb.Core;

namespace EntglDb.Core.Sync
{
    public class LastWriteWinsConflictResolver : IConflictResolver
    {
        public ConflictResolutionResult Resolve(Document? local, OplogEntry remote)
        {
            // If no local document exists, always apply remote change
            if (local == null)
            {
                // Construct new document from oplog entry
                var content = remote.Payload ?? default;
                var newDoc = new Document(remote.Collection, remote.Key, content, remote.Timestamp, remote.Operation == OperationType.Delete);
                return ConflictResolutionResult.Apply(newDoc);
            }

            // If local exists, compare timestamps
            if (remote.Timestamp.CompareTo(local.UpdatedAt) > 0)
            {
                // Remote is newer, apply it
                var content = remote.Payload ?? default;
                var newDoc = new Document(remote.Collection, remote.Key, content, remote.Timestamp, remote.Operation == OperationType.Delete);
                return ConflictResolutionResult.Apply(newDoc);
            }

            // Local is newer or equal, ignore remote
            return ConflictResolutionResult.Ignore();
        }
    }
}
