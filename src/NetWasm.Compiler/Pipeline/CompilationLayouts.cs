using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationLayouts(ManagedLayoutSnapshot snapshot)
{
    public ManagedLayoutSnapshot Snapshot { get; } = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));
}
