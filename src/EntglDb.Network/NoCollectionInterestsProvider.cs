using System.Collections.Generic;

namespace EntglDb.Network
{
    /// <summary>
    /// Advertises no collection interests, which is what a node that replicates nothing has to say.
    /// </summary>
    /// <remarks>
    /// Not a stub standing in for a real provider: interests exist so peers know which collections to push
    /// to this node, and a transport-only node wants none pushed to it. The document store supplies the real
    /// one when there is something to replicate, and registers it first.
    /// </remarks>
    public sealed class NoCollectionInterestsProvider : ILocalInterestsProvider
    {
        public IEnumerable<string> InterestedCollection { get; } = new string[0];
    }
}
