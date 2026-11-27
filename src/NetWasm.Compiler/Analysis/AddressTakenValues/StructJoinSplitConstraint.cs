// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IStructJoinSplitConstraint
{
    // Removes from split each join target with a source that is not split, until none is left.
    void Apply(HashSet<int> split, ILookup<int, int> sourcesByTarget);
}

// A join's target splits only with every value copied into it: the paths into the join then push nothing for it, and
// a source left whole would push its value where the join stores none. A source whose target stays whole loads its
// whole value into the join as before.
internal sealed class StructJoinSplitConstraint : IStructJoinSplitConstraint
{
    public void Apply(HashSet<int> split, ILookup<int, int> sourcesByTarget)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(sourcesByTarget);
        bool changed;
        do
        {
            changed = false;
            foreach (var group in sourcesByTarget)
            {
                if (split.Contains(group.Key) && !group.All(split.Contains)) changed |= split.Remove(group.Key);
            }
        }
        while (changed);
    }
}
