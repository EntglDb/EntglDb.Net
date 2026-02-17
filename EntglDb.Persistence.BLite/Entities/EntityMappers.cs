using System.Text.Json;
using EntglDb.Core;
using EntglDb.Core.Network;

namespace EntglDb.Persistence.BLite.Entities;

/// <summary>
/// Provides extension methods for mapping between BLite entities and domain models.
/// </summary>
public static class EntityMappers
{
    #region OplogEntity Mappers

    /// <summary>
    /// Converts an OplogEntry domain model to an OplogEntity for persistence.
    /// </summary>
    public static OplogEntity ToEntity(this OplogEntry entry)
    {
        return new OplogEntity
        {
            Id = Guid.NewGuid().ToString(), // Auto-generate technical key
            Collection = entry.Collection,
            Key = entry.Key,
            Operation = (int)entry.Operation,
            // Use empty string instead of null to avoid BLite BSON serialization issues
            PayloadJson = entry.Payload?.GetRawText() ?? "",
            TimestampPhysicalTime = entry.Timestamp.PhysicalTime,
            TimestampLogicalCounter = entry.Timestamp.LogicalCounter,
            TimestampNodeId = entry.Timestamp.NodeId,
            Hash = entry.Hash,
            PreviousHash = entry.PreviousHash
        };
    }

    /// <summary>
    /// Converts an OplogEntity to an OplogEntry domain model.
    /// </summary>
    public static OplogEntry ToDomain(this OplogEntity entity)
    {
        JsonElement? payload = null;
        // Treat empty string as null payload (Delete operations)
        if (!string.IsNullOrEmpty(entity.PayloadJson))
        {
            payload = JsonSerializer.Deserialize<JsonElement>(entity.PayloadJson);
        }

        return new OplogEntry(
            entity.Collection,
            entity.Key,
            (OperationType)entity.Operation,
            payload,
            new HlcTimestamp(entity.TimestampPhysicalTime, entity.TimestampLogicalCounter, entity.TimestampNodeId),
            entity.PreviousHash,
            entity.Hash);
    }

    /// <summary>
    /// Converts a collection of OplogEntity to OplogEntry domain models.
    /// </summary>
    public static IEnumerable<OplogEntry> ToDomain(this IEnumerable<OplogEntity> entities)
    {
        return entities.Select(e => e.ToDomain());
    }

    #endregion

    #region SnapshotMetadataEntity Mappers

    /// <summary>
    /// Converts a SnapshotMetadata domain model to a SnapshotMetadataEntity for persistence.
    /// </summary>
    public static SnapshotMetadataEntity ToEntity(this SnapshotMetadata metadata)
    {
        return new SnapshotMetadataEntity
        {
            Id = Guid.NewGuid().ToString(), // Auto-generate technical key
            NodeId = metadata.NodeId,
            TimestampPhysicalTime = metadata.TimestampPhysicalTime,
            TimestampLogicalCounter = metadata.TimestampLogicalCounter,
            Hash = metadata.Hash
        };
    }

    /// <summary>
    /// Converts a SnapshotMetadataEntity to a SnapshotMetadata domain model.
    /// </summary>
    public static SnapshotMetadata ToDomain(this SnapshotMetadataEntity entity)
    {
        return new SnapshotMetadata
        {
            NodeId = entity.NodeId,
            TimestampPhysicalTime = entity.TimestampPhysicalTime,
            TimestampLogicalCounter = entity.TimestampLogicalCounter,
            Hash = entity.Hash
        };
    }

    /// <summary>
    /// Converts a collection of SnapshotMetadataEntity to SnapshotMetadata domain models.
    /// </summary>
    public static IEnumerable<SnapshotMetadata> ToDomain(this IEnumerable<SnapshotMetadataEntity> entities)
    {
        return entities.Select(e => e.ToDomain());
    }

    #endregion

    #region RemotePeerEntity Mappers

    /// <summary>
    /// Converts a RemotePeerConfiguration domain model to a RemotePeerEntity for persistence.
    /// </summary>
    public static RemotePeerEntity ToEntity(this RemotePeerConfiguration config)
    {
        return new RemotePeerEntity
        {
            Id = Guid.NewGuid().ToString(), // Auto-generate technical key
            NodeId = config.NodeId,
            Address = config.Address,
            Type = (int)config.Type,
            OAuth2Json = config.OAuth2Json ?? "",
            IsEnabled = config.IsEnabled,
            InterestsJson = config.InterestingCollections.Count > 0
                ? JsonSerializer.Serialize(config.InterestingCollections)
                : ""
        };
    }

    /// <summary>
    /// Converts a RemotePeerEntity to a RemotePeerConfiguration domain model.
    /// </summary>
    public static RemotePeerConfiguration ToDomain(this RemotePeerEntity entity)
    {
        var config = new RemotePeerConfiguration
        {
            NodeId = entity.NodeId,
            Address = entity.Address,
            Type = (PeerType)entity.Type,
            OAuth2Json = entity.OAuth2Json,
            IsEnabled = entity.IsEnabled
        };

        if (!string.IsNullOrEmpty(entity.InterestsJson))
        {
            config.InterestingCollections = JsonSerializer.Deserialize<List<string>>(entity.InterestsJson) ?? [];
        }

        return config;
    }

    /// <summary>
    /// Converts a collection of RemotePeerEntity to RemotePeerConfiguration domain models.
    /// </summary>
    public static IEnumerable<RemotePeerConfiguration> ToDomain(this IEnumerable<RemotePeerEntity> entities)
    {
        return entities.Select(e => e.ToDomain());
    }

    #endregion
}
