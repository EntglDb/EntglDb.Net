using System.Threading.Tasks;

namespace EntglDb.Core.Cache
{
    public interface IDocumentCache
    {
        void Clear();
        Task<Document?> Get(string collection, string key);
        (long Hits, long Misses, int Size, double HitRate) GetStatistics();
        void Remove(string collection, string key);
        Task Set(string collection, string key, Document document);
    }
}