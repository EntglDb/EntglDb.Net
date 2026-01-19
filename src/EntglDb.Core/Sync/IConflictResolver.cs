using System.Text.Json;
using EntglDb.Core;

namespace EntglDb.Core.Sync;

public class ConflictResolutionResult
{
    public bool ShouldApply { get; }
    public Document? MergedDocument { get; }

    public ConflictResolutionResult(bool shouldApply, Document? mergedDocument)
    {
        ShouldApply = shouldApply;
        MergedDocument = mergedDocument;
    }

    public static ConflictResolutionResult Apply(Document document) => new(true, document);
    public static ConflictResolutionResult Ignore() => new(false, null);
}

public interface IConflictResolver
{
    ConflictResolutionResult Resolve(Document? local, OplogEntry remote);
}
