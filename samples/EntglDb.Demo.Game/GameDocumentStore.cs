using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EntglDb.Demo.Game;

public class GameDocumentStore : BLiteDocumentStore<GameDbContext>
{
    private const string HeroesCollection = "Heroes";
    private const string BattleLogsCollection = "BattleLogs";

    public GameDocumentStore(
        GameDbContext context,
        EntglDbMetaContext metaContext,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        IPendingChangesService pendingChangesService,
        ILogger<GameDocumentStore>? logger = null)
        : base(context, metaContext, configProvider, vectorClockService, pendingChangesService, new LastWriteWinsConflictResolver(), logger)
    {
        WatchCollection(HeroesCollection, context.Heroes, h => h.Id);
        WatchCollection(BattleLogsCollection, context.BattleLogs, b => b.Id);
    }

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken)
    {
        await UpsertEntityAsync(collection, key, content);
        await _context.SaveChangesAsync(cancellationToken);
    }

    protected override async Task ApplyContentToEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key, JsonElement Content)> documents, CancellationToken cancellationToken)
    {
        foreach (var (collection, key, content) in documents)
            await UpsertEntityAsync(collection, key, content);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertEntityAsync(string collection, string key, JsonElement content)
    {
        switch (collection)
        {
            case HeroesCollection:
                var hero = content.Deserialize<Hero>()!;
                hero.Id = key;
                if (await _context.Heroes.FindByIdAsync(key) != null)
                    await _context.Heroes.UpdateAsync(hero);
                else
                    await _context.Heroes.InsertAsync(hero);
                break;

            case BattleLogsCollection:
                var log = content.Deserialize<BattleLog>()!;
                log.Id = key;
                if (await _context.BattleLogs.FindByIdAsync(key) != null)
                    await _context.BattleLogs.UpdateAsync(log);
                else
                    await _context.BattleLogs.InsertAsync(log);
                break;

            default:
                throw new NotSupportedException($"Collection '{collection}' is not supported.");
        }
    }

    protected override async Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        return collection switch
        {
            HeroesCollection => SerializeEntity(await _context.Heroes.FindByIdAsync(key)),
            BattleLogsCollection => SerializeEntity(await _context.BattleLogs.FindByIdAsync(key)),
            _ => null
        };
    }

    protected override async Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken)
    {
        await DeleteEntityAsync(collection, key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    protected override async Task RemoveEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key)> documents, CancellationToken cancellationToken)
    {
        foreach (var (collection, key) in documents)
            await DeleteEntityAsync(collection, key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteEntityAsync(string collection, string key)
    {
        switch (collection)
        {
            case HeroesCollection:
                await _context.Heroes.DeleteAsync(key);
                break;
            case BattleLogsCollection:
                await _context.BattleLogs.DeleteAsync(key);
                break;
        }
    }

    protected override async Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken cancellationToken)
    {
        var results = new List<(string Key, JsonElement Content)>();
        switch (collection)
        {
            case HeroesCollection:
                await foreach (var h in _context.Heroes.FindAllAsync())
                    results.Add((h.Id, SerializeEntity(h)!.Value));
                break;
            case BattleLogsCollection:
                await foreach (var b in _context.BattleLogs.FindAllAsync())
                    results.Add((b.Id, SerializeEntity(b)!.Value));
                break;
        }
        return results;
    }

    private static JsonElement? SerializeEntity<T>(T? entity) where T : class
    {
        if (entity == null) return null;
        return JsonSerializer.SerializeToElement(entity);
    }
}
