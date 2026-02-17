using System;
using System.Collections.Generic;

namespace EntglDb.Core;

/// <summary>
/// Event arguments for when changes are applied to the peer store.
/// </summary>
public class ChangesAppliedEventArgs : EventArgs
{
    public IEnumerable<OplogEntry> Changes { get; }
    public ChangesAppliedEventArgs(IEnumerable<OplogEntry> changes)
    {
        Changes = changes;
    }
}
