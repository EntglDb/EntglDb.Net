using System.Text.Json.Serialization;
using EntglDb.Persistence.Snapshot;

namespace EntglDb.Persistence.Sqlite;

/// <summary>
/// JSON source-generation context for EntglDb.Persistence types.
/// Enables fully AOT-compatible, reflection-free serialization for snapshots.
/// All nested types (DocumentDto, OplogDto, SnapshotMetadataDto, RemotePeerDto) are
/// picked up automatically via the root SnapshotDto registration.
/// </summary>
[JsonSerializable(typeof(SnapshotDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
internal sealed partial class EntglDbPersistenceJsonContext : JsonSerializerContext
{
}
