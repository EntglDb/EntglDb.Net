using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BLite.Bson;
using BLite.Core.Collections;
using BLite.Core.Indexing;
using BLite.Core.Storage;
using BLite.Core.Transactions;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;

namespace EntglDb.Persistence.Blite.Internal;

internal abstract class BliteMapperBase<TId, TEntity> : IDocumentMapper<TId, TEntity> where TEntity : class
{
    protected readonly StorageEngine Storage;
    public string CollectionName { get; }

    protected BliteMapperBase(StorageEngine storage, string collectionName)
    {
        Storage = storage;
        CollectionName = collectionName;
    }

    public virtual IEnumerable<string> UsedKeys => GetSchema().GetAllKeys();

    public virtual BsonSchema GetSchema()
    {
        var schema = new BsonSchema { Title = CollectionName };
        foreach (var key in DefineKeys())
        {
            schema.Fields.Add(new BsonField { Name = key });
        }
        return schema;
    }

    protected abstract IEnumerable<string> DefineKeys();

    public abstract int Serialize(TEntity entity, BsonSpanWriter writer);
    public abstract TEntity Deserialize(BsonSpanReader reader);

    public abstract TId GetId(TEntity entity);
    public abstract void SetId(TEntity entity, TId id);
    public abstract IndexKey ToIndexKey(TId id);
    public abstract TId FromIndexKey(IndexKey key);

    protected void WriteJsonElement(ref BsonSpanWriter writer, string name, JsonElement element)
    {
        // Force key registration in global dictionary
        Storage.GetOrAddDictionaryEntry(name);
        
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var start = writer.BeginDocument(name);
                foreach (var prop in element.EnumerateObject())
                {
                    WriteJsonElement(ref writer, prop.Name, prop.Value);
                }
                writer.EndDocument(start);
                break;
            case JsonValueKind.Array:
                var arrayStart = writer.BeginArray(name);
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonElement(ref writer, i.ToString(), item);
                    i++;
                }
                writer.EndArray(arrayStart);
                break;
            case JsonValueKind.String:
                writer.WriteString(name, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out int i32)) writer.WriteInt32(name, i32);
                else if (element.TryGetInt64(out long i64)) writer.WriteInt64(name, i64);
                else writer.WriteDouble(name, element.GetDouble());
                break;
            case JsonValueKind.True:
                writer.WriteBoolean(name, true);
                break;
            case JsonValueKind.False:
                writer.WriteBoolean(name, false);
                break;
            case JsonValueKind.Null:
                writer.WriteNull(name);
                break;
        }
    }
}

internal class DocumentMapper : BliteMapperBase<string, Document>
{
    public DocumentMapper(StorageEngine storage, string collection) : base(storage, collection) { }

    protected override IEnumerable<string> DefineKeys() => new[] { "_id", "pt", "lc", "ni", "d", "c" };

    public override int Serialize(Document entity, BsonSpanWriter writer)
    {
        // Ensure static keys are registered
        foreach (var key in DefineKeys()) Storage.GetOrAddDictionaryEntry(key);

        var start = writer.BeginDocument();
        writer.WriteString("_id", entity.Key); // Map Key to _id for primary indexing
        writer.WriteInt64("pt", entity.UpdatedAt.PhysicalTime);
        writer.WriteInt32("lc", entity.UpdatedAt.LogicalCounter);
        writer.WriteString("ni", entity.UpdatedAt.NodeId);
        writer.WriteBoolean("d", entity.IsDeleted);
        
        WriteJsonElement(ref writer, "c", entity.Content);
        
        writer.EndDocument(start);
        return writer.Position;
    }

    public override Document Deserialize(BsonSpanReader reader)
    {
        string? key = null;
        long pt = 0;
        int lc = 0;
        string? ni = null;
        bool isDeleted = false;
        JsonElement content = default;

        reader.ReadDocumentSize();
        while (reader.Remaining > 1) // 1 byte for EOD
        {
            var type = reader.ReadBsonType();
            if (type == BsonType.EndOfDocument) break;
            var name = reader.ReadElementHeader();
            switch (name)
            {
                case "_id": key = reader.ReadString(); break;
                case "pt": pt = reader.ReadInt64(); break;
                case "lc": lc = reader.ReadInt32(); break;
                case "ni": ni = reader.ReadString(); break;
                case "d": isDeleted = reader.ReadBoolean(); break;
                case "c": 
                    content = ReadAsJsonElement(ref reader, type);
                    break;
                default:
                    reader.SkipValue(type); 
                    break;
            }
        }

        return new Document(CollectionName, key!, content, new HlcTimestamp(pt, lc, ni!), isDeleted);
    }

    private JsonElement ReadAsJsonElement(ref BsonSpanReader reader, BsonType type)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteBsonToJson(ref reader, type, writer);
        writer.Flush();
        var bytes = stream.ToArray();
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    private void WriteBsonToJson(ref BsonSpanReader reader, BsonType type, Utf8JsonWriter writer)
    {
        switch (type)
        {
            case BsonType.Document:
                writer.WriteStartObject();
                reader.ReadDocumentSize();
                while (reader.Remaining > 1)
                {
                    var t = reader.ReadBsonType();
                    if (t == BsonType.EndOfDocument) break;
                    var name = reader.ReadElementHeader();
                    writer.WritePropertyName(name);
                    WriteBsonToJson(ref reader, t, writer);
                }
                writer.WriteEndObject();
                break;
            case BsonType.Array:
                writer.WriteStartArray();
                reader.ReadDocumentSize();
                while (reader.Remaining > 1)
                {
                    var t = reader.ReadBsonType();
                    if (t == BsonType.EndOfDocument) break;
                    reader.ReadElementHeader(); // Skip array index
                    WriteBsonToJson(ref reader, t, writer);
                }
                writer.WriteEndArray();
                break;
            case BsonType.String:
                writer.WriteStringValue(reader.ReadString());
                break;
            case BsonType.Int32:
                writer.WriteNumberValue(reader.ReadInt32());
                break;
            case BsonType.Int64:
                writer.WriteNumberValue(reader.ReadInt64());
                break;
            case BsonType.Double:
                writer.WriteNumberValue(reader.ReadDouble());
                break;
            case BsonType.Boolean:
                writer.WriteBooleanValue(reader.ReadBoolean());
                break;
            case BsonType.Null:
                writer.WriteNullValue();
                break;
            default:
                reader.SkipValue(type);
                writer.WriteNullValue();
                break;
        }
    }

    public override string GetId(Document entity) => entity.Key;
    public override void SetId(Document entity, string id) { }
    public override IndexKey ToIndexKey(string id) => IndexKey.Create(id);
    public override string FromIndexKey(IndexKey key) => key.As<string>();
}

internal class OplogMapper : BliteMapperBase<string, OplogEntry>
{
    public OplogMapper(StorageEngine storage) : base(storage, "_oplog") { }

    protected override IEnumerable<string> DefineKeys() => new[] { "_id", "c", "k", "o", "pt", "lc", "ni", "ph", "p" };

    public override int Serialize(OplogEntry entity, BsonSpanWriter writer)
    {
        // Ensure static keys are registered
        foreach (var key in DefineKeys()) Storage.GetOrAddDictionaryEntry(key);

        var start = writer.BeginDocument();
        writer.WriteString("_id", entity.Hash); // Map Hash to _id
        writer.WriteString("c", entity.Collection);
        writer.WriteString("k", entity.Key);
        writer.WriteInt32("o", (int)entity.Operation);
        writer.WriteInt64("pt", entity.Timestamp.PhysicalTime);
        writer.WriteInt32("lc", entity.Timestamp.LogicalCounter);
        writer.WriteString("ni", entity.Timestamp.NodeId);
        writer.WriteString("ph", entity.PreviousHash);
        
        if (entity.Payload.HasValue)
        {
            WriteJsonElement(ref writer, "p", entity.Payload.Value);
        }
        else
        {
            writer.WriteNull("p");
        }
        
        writer.EndDocument(start);
        return writer.Position;
    }

    public override OplogEntry Deserialize(BsonSpanReader reader)
    {
        string? hash = null;
        string? collection = null;
        string? key = null;
        EntglDb.Core.OperationType operation = EntglDb.Core.OperationType.Put;
        long pt = 0;
        int lc = 0;
        string? ni = null;
        string? previousHash = null;
        JsonElement? payload = null;

        reader.ReadDocumentSize();
        while (reader.Remaining > 1)
        {
            var type = reader.ReadBsonType();
            if (type == BsonType.EndOfDocument) break;
            var name = reader.ReadElementHeader();
            switch (name)
            {
                case "_id": hash = reader.ReadString(); break;
                case "c": collection = reader.ReadString(); break;
                case "k": key = reader.ReadString(); break;
                case "o": operation = (EntglDb.Core.OperationType)reader.ReadInt32(); break;
                case "pt": pt = reader.ReadInt64(); break;
                case "lc": lc = reader.ReadInt32(); break;
                case "ni": ni = reader.ReadString(); break;
                case "ph": previousHash = reader.ReadString(); break;
                case "p": 
                    if (type != BsonType.Null) payload = ReadAsJsonElement(ref reader, type);
                    break;
                default: reader.SkipValue(type); break;
            }
        }

        return new OplogEntry(collection!, key!, operation, payload, new HlcTimestamp(pt, lc, ni!), previousHash!, hash);
    }

    private JsonElement ReadAsJsonElement(ref BsonSpanReader reader, BsonType type)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteBsonToJson(ref reader, type, writer);
        writer.Flush();
        var bytes = stream.ToArray();
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    private void WriteBsonToJson(ref BsonSpanReader reader, BsonType type, Utf8JsonWriter writer)
    {
        switch (type)
        {
            case BsonType.Document:
                writer.WriteStartObject();
                reader.ReadDocumentSize();
                while (reader.Remaining > 1)
                {
                    var t = reader.ReadBsonType();
                    if (t == BsonType.EndOfDocument) break;
                    var name = reader.ReadElementHeader();
                    writer.WritePropertyName(name);
                    WriteBsonToJson(ref reader, t, writer);
                }
                writer.WriteEndObject();
                break;
            case BsonType.Array:
                writer.WriteStartArray();
                reader.ReadDocumentSize();
                while (reader.Remaining > 1)
                {
                    var t = reader.ReadBsonType();
                    if (t == BsonType.EndOfDocument) break;
                    reader.ReadElementHeader(); // Skip array index
                    WriteBsonToJson(ref reader, t, writer);
                }
                writer.WriteEndArray();
                break;
            case BsonType.String:
                writer.WriteStringValue(reader.ReadString());
                break;
            case BsonType.Int32:
                writer.WriteNumberValue(reader.ReadInt32());
                break;
            case BsonType.Int64:
                writer.WriteNumberValue(reader.ReadInt64());
                break;
            case BsonType.Double:
                writer.WriteNumberValue(reader.ReadDouble());
                break;
            case BsonType.Boolean:
                writer.WriteBooleanValue(reader.ReadBoolean());
                break;
            case BsonType.Null:
                writer.WriteNullValue();
                break;
            default:
                reader.SkipValue(type);
                writer.WriteNullValue();
                break;
        }
    }

    public override string GetId(OplogEntry entity) => entity.Hash;
    public override void SetId(OplogEntry entity, string id) { }
    public override IndexKey ToIndexKey(string id) => IndexKey.Create(id);
    public override string FromIndexKey(IndexKey key) => key.As<string>();
}

internal class RemotePeerConfigurationMapper : BliteMapperBase<string, RemotePeerConfiguration>
{
    public RemotePeerConfigurationMapper(StorageEngine storage) : base(storage, "_remote_peers") { }

    protected override IEnumerable<string> DefineKeys() => new[] { "_id", "a", "t", "o", "e" };

    public override int Serialize(RemotePeerConfiguration entity, BsonSpanWriter writer)
    {
        // Ensure static keys are registered
        foreach (var key in DefineKeys()) Storage.GetOrAddDictionaryEntry(key);

        var start = writer.BeginDocument();
        writer.WriteString("_id", entity.NodeId); // Map NodeId to _id
        writer.WriteString("a", entity.Address);
        writer.WriteInt32("t", (int)entity.Type);
        writer.WriteString("o", entity.OAuth2Json ?? string.Empty);
        writer.WriteBoolean("e", entity.IsEnabled);
        writer.EndDocument(start);
        return writer.Position;
    }

    public override RemotePeerConfiguration Deserialize(BsonSpanReader reader)
    {
        var config = new RemotePeerConfiguration();

        reader.ReadDocumentSize();
        while (reader.Remaining > 1)
        {
            var type = reader.ReadBsonType();
            if (type == BsonType.EndOfDocument) break;
            var name = reader.ReadElementHeader();
            switch (name)
            {
                case "_id": config.NodeId = reader.ReadString(); break;
                case "a": config.Address = reader.ReadString(); break;
                case "t": config.Type = (PeerType)reader.ReadInt32(); break;
                case "o": config.OAuth2Json = reader.ReadString(); break;
                case "e": config.IsEnabled = reader.ReadBoolean(); break;
                default: reader.SkipValue(type); break;
            }
        }

        return config;
    }

    public override string GetId(RemotePeerConfiguration entity) => entity.NodeId;
    public override void SetId(RemotePeerConfiguration entity, string id) { entity.NodeId = id; }
    public override IndexKey ToIndexKey(string id) => IndexKey.Create(id);
    public override string FromIndexKey(IndexKey key) => key.As<string>();
}
