using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Pipeline;

/// <summary>The methods each by-ref analysis read effects from, as a call graph.</summary>
internal static class ByRefCallGraph
{
    // Whether a call from any of the methods reaches the target, through any number of calls.
    public static bool Reaches(ImmutableArray<string> from, string target, IReadOnlyDictionary<string, ImmutableArray<string>> callees)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(callees);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(from);
        while (pending.TryPop(out var next))
        {
            if (string.Equals(next, target, StringComparison.Ordinal)) return true;
            if (!seen.Add(next) || !callees.TryGetValue(next, out var onward)) continue;
            foreach (var callee in onward) pending.Push(callee);
        }
        return false;
    }
}
