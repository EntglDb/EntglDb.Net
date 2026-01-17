using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EntglDb.Core.Sync
{
    public class RecursiveNodeMergeConflictResolver : IConflictResolver
    {
        public ConflictResolutionResult Resolve(Document? local, OplogEntry remote)
        {
            if (local == null)
            {
                var content = remote.Payload ?? default;
                var newDoc = new Document(remote.Collection, remote.Key, content, remote.Timestamp, remote.Operation == OperationType.Delete);
                return ConflictResolutionResult.Apply(newDoc);
            }

            if (remote.Operation == OperationType.Delete)
            {
                if (remote.Timestamp.CompareTo(local.UpdatedAt) > 0)
                {
                    var newDoc = new Document(remote.Collection, remote.Key, default, remote.Timestamp, true);
                    return ConflictResolutionResult.Apply(newDoc);
                }
                return ConflictResolutionResult.Ignore();
            }

            var localJson = local.Content;
            var remoteJson = remote.Payload ?? default;
            var localTs = local.UpdatedAt;
            var remoteTs = remote.Timestamp;

            if (localJson.ValueKind == JsonValueKind.Undefined) return ConflictResolutionResult.Apply(new Document(remote.Collection, remote.Key, remoteJson, remoteTs, false));
            if (remoteJson.ValueKind == JsonValueKind.Undefined) return ConflictResolutionResult.Ignore();

            // Optimization: Use ArrayBufferWriter (Net6.0) or MemoryStream (NS2.0)
            // Utf8JsonWriter works with both, but ArrayBufferWriter is more efficient for high throughput.
            
            JsonElement mergedDocJson;

#if NET6_0_OR_GREATER
            var bufferWriter = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                MergeJson(writer, localJson, localTs, remoteJson, remoteTs);
            }
            mergedDocJson = JsonDocument.Parse(bufferWriter.WrittenMemory).RootElement;
#else
            using (var ms = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(ms))
                {
                    MergeJson(writer, localJson, localTs, remoteJson, remoteTs);
                }
                // Parse expects ReadOnlyMemory or Byte array
                mergedDocJson = JsonDocument.Parse(ms.ToArray()).RootElement;
            }
#endif
            
            var maxTimestamp = remoteTs.CompareTo(localTs) > 0 ? remoteTs : localTs;
            var mergedDoc = new Document(remote.Collection, remote.Key, mergedDocJson, maxTimestamp, false);
            return ConflictResolutionResult.Apply(mergedDoc);
        }

        private void MergeJson(Utf8JsonWriter writer, JsonElement local, HlcTimestamp localTs, JsonElement remote, HlcTimestamp remoteTs)
        {
            if (local.ValueKind != remote.ValueKind)
            {
                // Winner writes
                if (remoteTs.CompareTo(localTs) > 0) remote.WriteTo(writer);
                else local.WriteTo(writer);
                return;
            }

            switch (local.ValueKind)
            {
                case JsonValueKind.Object:
                    MergeObjects(writer, local, localTs, remote, remoteTs);
                    break;
                case JsonValueKind.Array:
                    MergeArrays(writer, local, localTs, remote, remoteTs);
                    break;
                default:
                    // Primitives
                    if (local.GetRawText() == remote.GetRawText()) 
                    {
                        local.WriteTo(writer);
                    }
                    else
                    {
                        if (remoteTs.CompareTo(localTs) > 0) remote.WriteTo(writer);
                        else local.WriteTo(writer);
                    }
                    break;
            }
        }

        private void MergeObjects(Utf8JsonWriter writer, JsonElement local, HlcTimestamp localTs, JsonElement remote, HlcTimestamp remoteTs)
        {
            writer.WriteStartObject();

            // We need to iterate keys. To avoid double iteration efficiently, we can use a dictionary for the UNION of keys.
            // But populating a dictionary is effectively what we did before.
            // Can we do better?
            // Yes: Iterate Local, write merged/local. Track handled keys. Then iterate Remote, write remaining.
            
            var processedKeys = new HashSet<string>();

            foreach (var prop in local.EnumerateObject())
            {
                var key = prop.Name;
                processedKeys.Add(key); // Mark as processed

                writer.WritePropertyName(key);

                if (remote.TryGetProperty(key, out var remoteVal))
                {
                    // Collision -> Merge
                    MergeJson(writer, prop.Value, localTs, remoteVal, remoteTs);
                }
                else
                {
                    // Only local
                    prop.Value.WriteTo(writer);
                }
            }

            foreach (var prop in remote.EnumerateObject())
            {
                if (!processedKeys.Contains(prop.Name))
                {
                    // New from remote
                    writer.WritePropertyName(prop.Name);
                    prop.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        private void MergeArrays(Utf8JsonWriter writer, JsonElement local, HlcTimestamp localTs, JsonElement remote, HlcTimestamp remoteTs)
        {
            // Heuristic check
            bool localIsObj = HasObjects(local);
            bool remoteIsObj = HasObjects(remote);

            if (!localIsObj && !remoteIsObj)
            {
                // Primitive LWW
                if (remoteTs.CompareTo(localTs) > 0) remote.WriteTo(writer);
                else local.WriteTo(writer);
                return;
            }

            if (localIsObj != remoteIsObj)
            {
                 // Mixed mistmatch LWW
                 if (remoteTs.CompareTo(localTs) > 0) remote.WriteTo(writer);
                 else local.WriteTo(writer);
                 return;
            }

            // Both Object Arrays - ID strategy
            // 1. Build map of IDs (JsonElement is struct, cheap to hold)
            var localMap = MapById(local);
            var remoteMap = MapById(remote);

            if (localMap == null || remoteMap == null)
            {
                // Fallback LWW
                if (remoteTs.CompareTo(localTs) > 0) remote.WriteTo(writer);
                else local.WriteTo(writer);
                return;
            }

            writer.WriteStartArray();

            // We want to write Union of items by ID.
            // To preserve some semblance of order (or just determinism), we can iterate local IDs first, then remote new IDs.
            // Or just use the dictionary values.
            
            // NOTE: We cannot simply write to writer inside the map loop if we are creating a merged map.
            // Let's iterate the union of keys similar to Objects.
            
            var processedIds = new HashSet<string>();

            // 1. Process Local Items (Merge or Write)
            foreach (var kvp in localMap)
            {
                var id = kvp.Key;
                var localItem = kvp.Value;
                processedIds.Add(id);

                if (remoteMap.TryGetValue(id, out var remoteItem))
                {
                    // Merge recursively
                    MergeJson(writer, localItem, localTs, remoteItem, remoteTs);
                }
                else
                {
                    // Keep local item
                    localItem.WriteTo(writer);
                }
            }

            // 2. Process New Remote Items
            foreach (var kvp in remoteMap)
            {
                if (!processedIds.Contains(kvp.Key))
                {
                    kvp.Value.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
        }

        private bool HasObjects(JsonElement array)
        {
            if (array.GetArrayLength() == 0) return false;
            // Check first item as heuristic
            return array[0].ValueKind == JsonValueKind.Object;
        }

        private Dictionary<string, JsonElement>? MapById(JsonElement array)
        {
            var map = new Dictionary<string, JsonElement>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return null; // Abort mixed

                string? id = null;
                if (item.TryGetProperty("id", out var p)) id = p.ToString();
                else if (item.TryGetProperty("_id", out var p2)) id = p2.ToString();

                if (id == null) return null; // Missing ID
                if (map.ContainsKey(id)) return null; // Duplicate ID

                map[id] = item;
            }
            return map;
        }
    }
}
