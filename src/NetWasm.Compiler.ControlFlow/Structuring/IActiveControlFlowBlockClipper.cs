using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IActiveControlFlowBlockClipper
{
    ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> allowed,
        IReadOnlySet<int> activePath,
        IReadOnlySet<int> retained);
}
