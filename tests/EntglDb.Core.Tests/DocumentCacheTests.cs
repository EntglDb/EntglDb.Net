using System.Text.Json;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Cache;
using EntglDb.Core.Network;
using Xunit;

namespace EntglDb.Core.Tests;

/// <summary>
/// Tests for DocumentCache with (collection, key) ValueTuple key.
/// Strict on key isolation, LRU eviction ordering, and update semantics.
/// </summary>
public class DocumentCacheTests
{
    private static DocumentCache CreateCache(int maxSize)
    {
        var config = new PeerNodeConfiguration { MaxDocumentCacheSize = maxSize };
        var provider = new StaticPeerNodeConfigurationProvider(config);
        return new DocumentCache(provider);
    }

    private static Document MakeDoc(string collection, string key, string json = "{}")
    {
        var elem = JsonDocument.Parse(json).RootElement;
        return new Document(collection, key, elem, new HlcTimestamp(1, 0, "n"), false);
    }

    // ── Miss ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_NonExistentEntry_ReturnsNull()
    {
        var cache = CreateCache(10);

        var result = await cache.Get("col", "missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task Get_AfterSet_DifferentCollectionSameKey_ReturnsNull()
    {
        var cache = CreateCache(10);
        await cache.Set("A", "k1", MakeDoc("A", "k1"));

        // (B, k1) was never stored
        Assert.Null(await cache.Get("B", "k1"));
    }

    [Fact]
    public async Task Get_AfterSet_SameCollectionDifferentKey_ReturnsNull()
    {
        var cache = CreateCache(10);
        await cache.Set("col", "k1", MakeDoc("col", "k1"));

        // (col, k2) was never stored
        Assert.Null(await cache.Get("col", "k2"));
    }

    // ── Key isolation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_SameKeyDifferentCollections_StoresIndependently()
    {
        var cache = CreateCache(10);
        var docA = MakeDoc("A", "k1", """{"v":1}""");
        var docB = MakeDoc("B", "k1", """{"v":2}""");

        await cache.Set("A", "k1", docA);
        await cache.Set("B", "k1", docB);

        var resultA = await cache.Get("A", "k1");
        var resultB = await cache.Get("B", "k1");

        Assert.NotNull(resultA);
        Assert.NotNull(resultB);
        Assert.Equal(1, resultA!.Content.GetProperty("v").GetInt32());
        Assert.Equal(2, resultB!.Content.GetProperty("v").GetInt32());
    }

    [Fact]
    public async Task Set_SameCollectionDifferentKeys_StoresIndependently()
    {
        var cache = CreateCache(10);
        var docA = MakeDoc("col", "k1", """{"v":10}""");
        var docB = MakeDoc("col", "k2", """{"v":20}""");

        await cache.Set("col", "k1", docA);
        await cache.Set("col", "k2", docB);

        var r1 = await cache.Get("col", "k1");
        var r2 = await cache.Get("col", "k2");

        Assert.Equal(10, r1!.Content.GetProperty("v").GetInt32());
        Assert.Equal(20, r2!.Content.GetProperty("v").GetInt32());
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_SameKey_OverwritesExistingValue()
    {
        var cache = CreateCache(10);
        await cache.Set("col", "k1", MakeDoc("col", "k1", """{"v":1}"""));
        await cache.Set("col", "k1", MakeDoc("col", "k1", """{"v":99}"""));

        var result = await cache.Get("col", "k1");

        Assert.NotNull(result);
        Assert.Equal(99, result!.Content.GetProperty("v").GetInt32());
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_ExistingEntry_SubsequentGetReturnsNull()
    {
        var cache = CreateCache(10);
        await cache.Set("col", "k1", MakeDoc("col", "k1"));

        cache.Remove("col", "k1");

        Assert.Null(await cache.Get("col", "k1"));
    }

    [Fact]
    public void Remove_NonExistentEntry_DoesNotThrow()
    {
        var cache = CreateCache(10);

        var ex = Record.Exception(() => cache.Remove("col", "ghost"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Remove_OnlyRemovesExactKey_DoesNotAffectOthers()
    {
        var cache = CreateCache(10);
        await cache.Set("col", "k1", MakeDoc("col", "k1"));
        await cache.Set("col", "k2", MakeDoc("col", "k2"));
        await cache.Set("other", "k1", MakeDoc("other", "k1"));

        cache.Remove("col", "k1");

        Assert.Null(await cache.Get("col", "k1"));
        Assert.NotNull(await cache.Get("col", "k2"));          // same collection, different key
        Assert.NotNull(await cache.Get("other", "k1"));        // same key, different collection
    }

    // ── LRU eviction ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_ExceedsCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = CreateCache(2);
        await cache.Set("c", "k1", MakeDoc("c", "k1"));
        await cache.Set("c", "k2", MakeDoc("c", "k2"));

        // k1 is the LRU; adding k3 must evict k1
        await cache.Set("c", "k3", MakeDoc("c", "k3"));

        Assert.Null(await cache.Get("c", "k1"));            // evicted
        Assert.NotNull(await cache.Get("c", "k2"));         // still present
        Assert.NotNull(await cache.Get("c", "k3"));         // just added
    }

    [Fact]
    public async Task Get_BumpsEntryToFront_PreventingItsEviction()
    {
        var cache = CreateCache(2);
        await cache.Set("c", "k1", MakeDoc("c", "k1"));
        await cache.Set("c", "k2", MakeDoc("c", "k2"));

        // Touch k1 → makes k2 the LRU
        await cache.Get("c", "k1");

        // k3 must evict k2 (LRU), not k1 (recently accessed)
        await cache.Set("c", "k3", MakeDoc("c", "k3"));

        Assert.NotNull(await cache.Get("c", "k1"));         // protected by recent access
        Assert.Null(await cache.Get("c", "k2"));            // true LRU — evicted
        Assert.NotNull(await cache.Get("c", "k3"));         // just added
    }

    [Fact]
    public async Task Set_Update_BumpsEntryToFront_PreventingItsEviction()
    {
        var cache = CreateCache(2);
        await cache.Set("c", "k1", MakeDoc("c", "k1"));
        await cache.Set("c", "k2", MakeDoc("c", "k2"));

        // Re-set k1 (update) → moves k1 to front; k2 becomes LRU
        await cache.Set("c", "k1", MakeDoc("c", "k1", """{"updated":true}"""));

        await cache.Set("c", "k3", MakeDoc("c", "k3")); // should evict k2

        Assert.NotNull(await cache.Get("c", "k1"));
        Assert.Null(await cache.Get("c", "k2"));
        Assert.NotNull(await cache.Get("c", "k3"));
    }

    [Fact]
    public async Task Set_FillToCapacityThenInsertMany_NeverExceedsCapacity()
    {
        int maxSize = 3;
        var cache = CreateCache(maxSize);
        int totalInserts = 20;

        for (int i = 0; i < totalInserts; i++)
        {
            await cache.Set("c", $"k{i}", MakeDoc("c", $"k{i}"));
        }

        // After 20 inserts with capacity 3, only the last 3 (k17, k18, k19) should be present.
        // Earlier entries must have been evicted.
        int presentCount = 0;
        for (int i = 0; i < totalInserts; i++)
        {
            if (await cache.Get("c", $"k{i}") != null)
                presentCount++;
        }

        Assert.Equal(maxSize, presentCount);
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var cache = CreateCache(10);
        await cache.Set("c", "k1", MakeDoc("c", "k1"));
        await cache.Set("c", "k2", MakeDoc("c", "k2"));

        cache.Clear();

        Assert.Null(await cache.Get("c", "k1"));
        Assert.Null(await cache.Get("c", "k2"));
    }
}
