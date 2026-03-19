using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            // Store normalized (key-sorted) JSON so cross-node payload comparisons are reliable
            PayloadJson = entry.Payload.HasValue ? NormalizeJson(entry.Payload.Value) : "",
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

    #region DocumentMetadataEntity Helpers

    /// <summary>
    /// Creates a DocumentMetadataEntity from collection, key, timestamp, deleted state, and optional content.
    /// Used for tracking document sync state.
    /// </summary>
    public static DocumentMetadataEntity CreateDocumentMetadata(
        string collection, string key, HlcTimestamp timestamp, bool isDeleted = false, JsonElement? content = null)
    {
        return new DocumentMetadataEntity
        {
            Id = $"{collection}/{key}",
            Collection = collection,
            Key = key,
            HlcPhysicalTime = timestamp.PhysicalTime,
            HlcLogicalCounter = timestamp.LogicalCounter,
            HlcNodeId = timestamp.NodeId,
            IsDeleted = isDeleted,
            ContentHash = isDeleted ? "" : ComputeContentHash(content)
        };
    }

    /// <summary>
    /// Renders a JsonElement as canonical (key-sorted, compact) JSON string.
    /// The oplog stores payloads in this form; CDC writes normalize on the way in.
    /// </summary>
    public static string NormalizeJson(JsonElement element)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
        WriteNormalizedJson(writer, element);
        writer.Flush();
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// Computes SHA-256 hash directly from an already-normalized JSON string.
    /// Use when the payload was loaded from the oplog (already canonical) to avoid redundant normalization.
    /// </summary>
    public static string ComputeContentHashFromNormalized(string normalizedJson)
    {
        if (string.IsNullOrEmpty(normalizedJson)) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a SHA-256 hash of the canonical (key-sorted) JSON representation of a document.
    /// Returns an empty string for null content (deletes).
    /// </summary>
    public static string ComputeContentHash(JsonElement? content)
    {
        if (content == null) return "";

        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
        WriteNormalizedJson(writer, content.Value);
        writer.Flush();

        var hash = SHA256.HashData(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteNormalizedJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteNormalizedJson(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteNormalizedJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default: // Null or Undefined
                writer.WriteNullValue();
                break;
        }
    }

    #endregion
}
