using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core;

public class ChangesAppliedEventArgs : EventArgs
{
    public IEnumerable<OplogEntry> Changes { get; }
    public ChangesAppliedEventArgs(IEnumerable<OplogEntry> changes)
    {
        Changes = changes;
    }
}
